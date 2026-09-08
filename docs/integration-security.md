# Critérios de Segurança para Consumo de Serviços

> Status: preenchido (issues #17 e #27, diferencial). Documento de critério — assim como `docs/cost-estimate.md` (issue #25) descreve infraestrutura-alvo sem exigir deploy real, este documento descreve o critério de segurança e a abordagem sugerida sem exigir implementação de código. Decisão fechada com Felipe e o Arquiteto Adjunto em 2026-09-08: para uma integração serviço-a-serviço (não usuário final), documentar a expectativa é suficiente para atender o item do desafio ("Critérios de segurança para consumo (integração) de serviços") — código fica como evolução futura, admitida incrementalmente se o tempo permitir.

## Autenticação e autorização entre serviços

**Natureza do problema**: Lançamentos e Consolidado se consomem entre si (evento assíncrono) e são consumidos externamente via HTTP — mas o consumidor é outro serviço/sistema, não um usuário final autenticando-se numa UI. Isso descarta por design mecanismos de identidade de usuário (Identity, OAuth2 authorization code, sessão) — o problema é "este chamador está autorizado a integrar com o serviço", não "quem é a pessoa por trás do request".

**Estado atual**: os dois serviços são hoje publicamente acessíveis sem controle de acesso — `POST /lancamentos` inclusive (já registrado em `docs/transition-architecture.md`). Este documento não muda esse estado; ele registra o critério e a abordagem-alvo.

**Critério sugerido (não implementado)**: chave compartilhada por serviço (API key), validada em middleware simples — mecanismo fechado com o Arquiteto Adjunto em 2026-09-07 (issue #17), reproduzido aqui como referência de design:

```csharp
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/health"))
    {
        var chave = context.Request.Headers["X-Api-Key"].ToString();
        if (chave != apiKeyEsperada)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next();
});
```

- Direto no `Program.cs` de cada serviço — sem Identity, sem `AuthenticationHandler` customizado, sem `IEndpointFilter`/`MapGroup`. Para no máximo 4 endpoints de negócio a proteger, qualquer mecanismo mais elaborado é complexidade desnecessária (KISS).
- `apiKeyEsperada` viria de configuração, mesmo mecanismo já em uso para segredos (`.env`/`docker-compose.yml`, ADR-006) — só mais uma variável.
- `/health/live` e `/health/ready` continuam sempre abertos (excluídos por prefixo de path) — health check não deve depender de credencial.

**Por que não mais que isso (JWT, OAuth2 client-credentials, mTLS)**: desproporcional para 2 serviços internos com baixo número de endpoints, no contexto de um desafio técnico. Evoluiria para OAuth2 client-credentials ou mTLS se a integração cruzasse um limite de confiança real (ex.: consumidor externo de terceiros, múltiplos serviços com necessidade de rotação de credencial centralizada) — não é o caso hoje.

**Por que documentação basta aqui, e não exigiu retomar a issue #17**: desde que o mecanismo foi fechado (07/09), `tests/EndToEnd.Tests` passou a incluir testes de resiliência e carga (issues #19/#20) que batem diretamente nos endpoints HTTP dos dois serviços — exigir a API key agora quebraria essas suítes já mergeadas até o header ser injetado em cada chamada. O custo de retrofit não se justifica como diferencial neste estágio do orçamento de tempo, com obrigatórios (#23/#24) ainda em fechamento. Ver `docs/transition-architecture.md` para o registro formal desse adiamento.

## Proteção de dados em trânsito e em repouso

**Em trânsito**: localmente (Docker Compose), a comunicação entre os serviços e Postgres/RabbitMQ é HTTP/AMQP simples, sem TLS — aceitável para a implementação de referência local (rede interna do Compose, não exposta). Na arquitetura-alvo (`docs/architecture.md`), Azure Container Apps, Postgres Flexible Server e Service Bus aplicam TLS por padrão em trânsito — resolvido pela infraestrutura gerenciada, não exige trabalho adicional de aplicação.

**Em repouso**: localmente, o volume do Postgres não é criptografado (Docker volume padrão). Na arquitetura-alvo, o Postgres Flexible Server criptografa dados em repouso por padrão (gerenciado pela Azure). Segredos (credenciais, chaves) já têm critério próprio registrado no ADR-006 (`.env` local / Azure Key Vault no alvo) — não duplicado aqui.

Mesmo padrão das seções acima: nenhuma mudança de infraestrutura é necessária hoje — o critério já é satisfeito pela arquitetura-alvo já documentada, e o adiamento (rodar sem TLS/criptografia localmente) é aceito conscientemente para a implementação de referência, coerente com o restante do roadmap de evolução em `docs/transition-architecture.md`.
