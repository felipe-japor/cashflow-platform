using Lancamentos.Domain.Exceptions;

namespace Lancamentos.Domain.Tests;

public class TransactionTests
{
    [Fact]
    public void Deve_criar_lancamento_com_as_propriedades_informadas()
    {
        var data = new DateOnly(2026, 9, 4);

        var transaction = new Transaction(data, TipoLancamento.Credito, 150.75m, "Venda à vista");

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(data, transaction.Data);
        Assert.Equal(TipoLancamento.Credito, transaction.Tipo);
        Assert.Equal(150.75m, transaction.Valor);
        Assert.Equal("Venda à vista", transaction.Descricao);
    }

    [Fact]
    public void Cada_lancamento_recebe_um_identificador_proprio()
    {
        var data = new DateOnly(2026, 9, 4);

        var primeiro = new Transaction(data, TipoLancamento.Debito, 10m, "Pagamento");
        var segundo = new Transaction(data, TipoLancamento.Debito, 10m, "Pagamento");

        Assert.NotEqual(primeiro.Id, segundo.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10.5)]
    public void Deve_rejeitar_lancamento_com_valor_menor_ou_igual_a_zero(decimal valor)
    {
        var data = new DateOnly(2026, 9, 4);

        Assert.Throws<LancamentoInvalidoException>(
            () => new Transaction(data, TipoLancamento.Credito, valor, "Lançamento inválido"));
    }

    [Fact]
    public void Deve_rejeitar_lancamento_com_tipo_fora_do_dominio_conhecido()
    {
        var data = new DateOnly(2026, 9, 4);
        const TipoLancamento tipoInvalido = (TipoLancamento)99;

        Assert.Throws<LancamentoInvalidoException>(
            () => new Transaction(data, tipoInvalido, 10m, "Lançamento inválido"));
    }
}
