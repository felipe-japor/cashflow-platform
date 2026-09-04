using Consolidado.Application.Ports;

namespace Consolidado.Application.Tests;

/// <summary>
/// Teste de fitness de arquitetura: Application define portas (interfaces) e não pode depender
/// da camada que as implementa — a direção da dependência é Infrastructure → Application, nunca
/// o contrário (inversão de dependência, decisão de estrutura da issue #6).
/// </summary>
public class CamadaAplicacaoTests
{
    [Fact]
    public void Consolidado_Application_nao_deve_referenciar_Infrastructure_nem_Api()
    {
        var referencias = typeof(IDailyConsolidationRepository).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("Consolidado.Infrastructure", referencias);
        Assert.DoesNotContain("Consolidado.Api", referencias);
        Assert.DoesNotContain("Shared.Contracts", referencias);
        Assert.Contains("Consolidado.Domain", referencias);
    }
}
