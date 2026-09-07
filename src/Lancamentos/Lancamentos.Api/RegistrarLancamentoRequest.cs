using Lancamentos.Domain;

namespace Lancamentos.Api;

/// <summary>
/// Corpo da requisição de <c>POST /lancamentos</c> (RF01). Usa o enum de domínio diretamente
/// (em vez de duplicar um enum próprio de API) — é o mesmo bounded context, sem fronteira de
/// integração aqui (diferente do contrato de evento em Shared.Contracts, que atravessa dois
/// bounded contexts e por isso precisa da própria cópia — ver docs/domain-mapping.md).
/// </summary>
public sealed record RegistrarLancamentoRequest(DateOnly Data, TipoLancamento Tipo, decimal Valor, string Descricao);
