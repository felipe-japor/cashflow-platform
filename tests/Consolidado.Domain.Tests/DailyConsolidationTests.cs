namespace Consolidado.Domain.Tests;

public class DailyConsolidationTests
{
    [Fact]
    public void Deve_criar_consolidado_do_dia_zerado()
    {
        var data = new DateOnly(2026, 9, 4);

        var consolidado = new DailyConsolidation(data);

        Assert.NotEqual(Guid.Empty, consolidado.Id);
        Assert.Equal(data, consolidado.Data);
        Assert.Equal(0m, consolidado.Creditos);
        Assert.Equal(0m, consolidado.Debitos);
        Assert.Equal(0m, consolidado.Saldo);
    }

    [Fact]
    public void Aplicar_credito_soma_em_creditos_e_no_saldo()
    {
        var consolidado = new DailyConsolidation(new DateOnly(2026, 9, 4));

        consolidado.Aplicar(TipoLancamento.Credito, 100m);

        Assert.Equal(100m, consolidado.Creditos);
        Assert.Equal(0m, consolidado.Debitos);
        Assert.Equal(100m, consolidado.Saldo);
    }

    [Fact]
    public void Aplicar_debito_soma_em_debitos_e_reduz_o_saldo()
    {
        var consolidado = new DailyConsolidation(new DateOnly(2026, 9, 4));
        consolidado.Aplicar(TipoLancamento.Credito, 100m);

        consolidado.Aplicar(TipoLancamento.Debito, 30m);

        Assert.Equal(100m, consolidado.Creditos);
        Assert.Equal(30m, consolidado.Debitos);
        Assert.Equal(70m, consolidado.Saldo);
    }

    [Fact]
    public void Aplicar_o_mesmo_delta_duas_vezes_soma_novamente_pois_a_entidade_nao_e_responsavel_pela_idempotencia()
    {
        // A idempotência do consumo (inbox pattern) é responsabilidade da camada de
        // infraestrutura (issue #12) — a entidade só expressa a regra de netting em si.
        var consolidado = new DailyConsolidation(new DateOnly(2026, 9, 4));

        consolidado.Aplicar(TipoLancamento.Credito, 50m);
        consolidado.Aplicar(TipoLancamento.Credito, 50m);

        Assert.Equal(100m, consolidado.Creditos);
    }
}
