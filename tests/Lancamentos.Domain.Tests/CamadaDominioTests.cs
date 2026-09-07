namespace Lancamentos.Domain.Tests;

/// <summary>
/// Testes de fitness de arquitetura: provam que a fronteira de camadas imposta pelo compilador
/// (projetos .csproj separados — decisão de estrutura da issue #6) está correta na direção
/// certa. Domain é o núcleo: não pode depender de nada fora de si mesmo.
/// </summary>
public class CamadaDominioTests
{
    [Fact]
    public void Lancamentos_Domain_nao_deve_referenciar_nenhuma_outra_camada_da_solucao()
    {
        var referencias = typeof(Transaction).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("Lancamentos.Application", referencias);
        Assert.DoesNotContain("Lancamentos.Infrastructure", referencias);
        Assert.DoesNotContain("Lancamentos.Api", referencias);
        Assert.DoesNotContain("Shared.Contracts", referencias);
    }
}
