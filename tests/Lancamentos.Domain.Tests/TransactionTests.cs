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
}
