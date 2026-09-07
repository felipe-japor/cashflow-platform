using Lancamentos.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lancamentos.Infrastructure.Tests;

/// <summary>
/// Prova a lógica de orquestração do worker de outbox (issue #10, #11): publica pendentes, não
/// republica os já publicados, incrementa o contador de falhas e para de tentar ao atingir o
/// limite (falha persistente) — sem broker real, via fake de <see cref="IEventPublisher"/>.
/// </summary>
public class OutboxPublisherWorkerTests
{
    private static IServiceScopeFactory CriarScopeFactory(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LancamentosDbContext>(options => options.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static LancamentosDbContext CriarDbContext(string dbName) =>
        new(new DbContextOptionsBuilder<LancamentosDbContext>().UseInMemoryDatabase(dbName).Options);

    private static OutboxPublisherWorker CriarWorker(string dbName, IEventPublisher publisher, OutboxPublisherOptions? options = null) =>
        new(CriarScopeFactory(dbName), publisher, Options.Create(options ?? new OutboxPublisherOptions()), NullLogger<OutboxPublisherWorker>.Instance);

    /// <summary>Fake controlado por uma fila de resultados: null publica com sucesso, uma
    /// exceção enfileirada é lançada naquela chamada.</summary>
    private sealed class EventPublisherFake : IEventPublisher
    {
        private readonly Queue<Exception?> _resultados;
        public List<(string EventType, string Payload)> Publicados { get; } = [];

        public EventPublisherFake(params Exception?[] resultados) => _resultados = new Queue<Exception?>(resultados);

        public Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken)
        {
            var falha = _resultados.Count > 0 ? _resultados.Dequeue() : null;
            if (falha is not null)
            {
                throw falha;
            }

            Publicados.Add((eventType, payload));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publica_eventos_pendentes_e_marca_como_publicado()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            setup.OutboxEvents.Add(new OutboxEvent("LancamentoRegistrado", "{}"));
            await setup.SaveChangesAsync();
        }

        var publisher = new EventPublisherFake();
        var worker = CriarWorker(dbName, publisher);

        await worker.PublicarPendentesAsync(CancellationToken.None);

        Assert.Single(publisher.Publicados);
        using var verificacao = CriarDbContext(dbName);
        var evento = Assert.Single(await verificacao.OutboxEvents.ToListAsync());
        Assert.NotNull(evento.PublicadoEmUtc);
        Assert.Equal(0, evento.TentativasFalhas);
        Assert.Null(evento.FalhaPersistenteEmUtc);
    }

    [Fact]
    public async Task Nao_republica_eventos_ja_publicados()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            var jaPublicado = new OutboxEvent("LancamentoRegistrado", "{}");
            jaPublicado.MarcarComoPublicado(DateTime.UtcNow);
            setup.OutboxEvents.Add(jaPublicado);
            await setup.SaveChangesAsync();
        }

        var publisher = new EventPublisherFake();
        var worker = CriarWorker(dbName, publisher);

        await worker.PublicarPendentesAsync(CancellationToken.None);

        Assert.Empty(publisher.Publicados);
    }

    [Fact]
    public async Task Incrementa_o_contador_de_tentativas_e_mantem_pendente_quando_a_publicacao_falha()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            setup.OutboxEvents.Add(new OutboxEvent("LancamentoRegistrado", "{}"));
            await setup.SaveChangesAsync();
        }

        var publisher = new EventPublisherFake(new InvalidOperationException("broker indisponível"));
        var worker = CriarWorker(dbName, publisher, new OutboxPublisherOptions { LimiteTentativas = 5 });

        await worker.PublicarPendentesAsync(CancellationToken.None);

        using var verificacao = CriarDbContext(dbName);
        var evento = Assert.Single(await verificacao.OutboxEvents.ToListAsync());
        Assert.Null(evento.PublicadoEmUtc);
        Assert.Equal(1, evento.TentativasFalhas);
        Assert.Null(evento.FalhaPersistenteEmUtc);
    }

    [Fact]
    public async Task Poll_seguinte_tenta_de_novo_um_evento_que_falhou_sem_atingir_o_limite()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            setup.OutboxEvents.Add(new OutboxEvent("LancamentoRegistrado", "{}"));
            await setup.SaveChangesAsync();
        }

        // Primeira falha, depois sucesso — prova que o próprio ciclo de poll seguinte é o
        // mecanismo de retry (issue #10), sem nenhuma lógica extra de reagendamento.
        var publisher = new EventPublisherFake(new InvalidOperationException("timeout"), null);
        var worker = CriarWorker(dbName, publisher, new OutboxPublisherOptions { LimiteTentativas = 5 });

        await worker.PublicarPendentesAsync(CancellationToken.None);
        await worker.PublicarPendentesAsync(CancellationToken.None);

        Assert.Single(publisher.Publicados);
        using var verificacao = CriarDbContext(dbName);
        var evento = Assert.Single(await verificacao.OutboxEvents.ToListAsync());
        Assert.NotNull(evento.PublicadoEmUtc);
        Assert.Equal(1, evento.TentativasFalhas);
    }

    [Fact]
    public async Task Marca_falha_persistente_ao_atingir_o_limite_de_tentativas_e_para_de_ser_consultado()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            setup.OutboxEvents.Add(new OutboxEvent("LancamentoRegistrado", "{}"));
            await setup.SaveChangesAsync();
        }

        var falhaSempre = () => new InvalidOperationException("broker indisponível");
        var publisher = new EventPublisherFake(falhaSempre(), falhaSempre());
        var worker = CriarWorker(dbName, publisher, new OutboxPublisherOptions { LimiteTentativas = 2 });

        await worker.PublicarPendentesAsync(CancellationToken.None);
        await worker.PublicarPendentesAsync(CancellationToken.None);

        using var verificacao = CriarDbContext(dbName);
        var evento = Assert.Single(await verificacao.OutboxEvents.ToListAsync());
        Assert.Equal(2, evento.TentativasFalhas);
        Assert.NotNull(evento.FalhaPersistenteEmUtc);

        // Uma terceira iteração não deve nem tentar publicar de novo — o evento em falha
        // persistente sai da consulta do worker (issue #11).
        await worker.PublicarPendentesAsync(CancellationToken.None);
        Assert.Empty(publisher.Publicados);
    }

    [Fact]
    public async Task Uma_falha_de_publicacao_nao_impede_os_demais_eventos_do_lote_de_serem_publicados()
    {
        var dbName = Guid.NewGuid().ToString();
        using (var setup = CriarDbContext(dbName))
        {
            setup.OutboxEvents.AddRange(
                new OutboxEvent("LancamentoRegistrado", "{\"n\":1}"),
                new OutboxEvent("LancamentoRegistrado", "{\"n\":2}"));
            await setup.SaveChangesAsync();
        }

        // Não importa qual dos dois falha primeiro (ordem não é o que está sob teste aqui) — o
        // que importa é que uma falha isolada não derruba o processamento do lote inteiro.
        var publisher = new EventPublisherFake(new InvalidOperationException("falha só num deles"), null);
        var worker = CriarWorker(dbName, publisher, new OutboxPublisherOptions { LimiteTentativas = 5 });

        await worker.PublicarPendentesAsync(CancellationToken.None);

        using var verificacao = CriarDbContext(dbName);
        var eventos = await verificacao.OutboxEvents.ToListAsync();
        Assert.Single(eventos, e => e.PublicadoEmUtc is not null);
        Assert.Single(eventos, e => e.PublicadoEmUtc is null && e.TentativasFalhas == 1);
    }
}
