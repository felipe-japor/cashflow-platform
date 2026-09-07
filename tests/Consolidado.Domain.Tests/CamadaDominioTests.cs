namespace Consolidado.Domain.Tests;

/// <summary>
/// Testes de fitness de arquitetura: provam que a fronteira de camadas imposta pelo compilador
/// (projetos .csproj separados — decisão de estrutura da issue #6) está correta na direção
/// certa. Domain é o núcleo: não pode depender de nada fora de si mesmo.
/// </summary>
public class CamadaDominioTests
{
    [Fact]
    public void Consolidado_Domain_nao_deve_referenciar_nenhuma_outra_camada_da_solucao()
    {
        var referencias = typeof(DailyConsolidation).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("Consolidado.Application", referencias);
        Assert.DoesNotContain("Consolidado.Infrastructure", referencias);
        Assert.DoesNotContain("Consolidado.Api", referencias);
        Assert.DoesNotContain("Shared.Contracts", referencias);
    }
}
