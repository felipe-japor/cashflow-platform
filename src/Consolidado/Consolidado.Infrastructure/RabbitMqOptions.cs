namespace Consolidado.Infrastructure;

/// <summary>
/// Configuração de conexão com o broker (RabbitMQ local — implementação de referência, ADR-004).
/// Vinculada à seção "RabbitMq" de configuração (appsettings/variáveis de ambiente).
/// </summary>
public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "cashflow.eventos";
    public string Queue { get; set; } = "consolidado.lancamento-registrado";
    public string RoutingKey { get; set; } = "LancamentoRegistrado";
}
