using Lancamentos.Application.Ports;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Teste de fitness de arquitetura: Application define portas (interfaces) e não pode depender
/// da camada que as implementa — a direção da dependência é Infrastructure → Application, nunca
/// o contrário (inversão de dependência, decisão de estrutura da issue #6).
/// </summary>
public class CamadaAplicacaoTests
{
    [Fact]
    public void Lancamentos_Application_nao_deve_referenciar_Infrastructure_nem_Api()
    {
        var referencias = typeof(ITransactionRepository).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("Lancamentos.Infrastructure", referencias);
        Assert.DoesNotContain("Lancamentos.Api", referencias);
        Assert.DoesNotContain("Shared.Contracts", referencias);
        Assert.Contains("Lancamentos.Domain", referencias);
    }
}
