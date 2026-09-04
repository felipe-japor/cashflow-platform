namespace Shared.Contracts;

/// <summary>
/// Contrato de integração entre os domínios Lançamentos (produtor) e Consolidado (consumidor).
/// Publicado pelo Lançamentos via outbox transacional (ADR-002) e consumido de forma idempotente
/// pelo Consolidado via inbox pattern (docs/domain-mapping.md). Carrega só o necessário para o
/// Consolidado atualizar o read model sem consultar o serviço de Lançamentos.
/// </summary>
/// <param name="TransactionId">Identidade do lançamento de origem — chave de idempotência no inbox do Consolidado.</param>
/// <param name="Data">Dia de negócio (UTC) ao qual o lançamento pertence — determina em qual consolidado diário o delta é aplicado.</param>
/// <param name="Tipo">Crédito ou débito — determina se o delta soma em Creditos ou Debitos.</param>
/// <param name="Valor">Valor do lançamento (delta a aplicar).</param>
public sealed record LancamentoRegistrado(
    Guid TransactionId,
    DateOnly Data,
    TipoLancamento Tipo,
    decimal Valor);
