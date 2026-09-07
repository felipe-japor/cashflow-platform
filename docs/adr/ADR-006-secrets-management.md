# ADR-006: Gestão de secrets (local via `.env`, alvo via Azure Key Vault)

> Status: aceito.

## Contexto

A issue #16 exige que nenhum segredo real fique em arquivo versionado, com connection string/credenciais vindo de variável de ambiente (ou equivalente) por ambiente. Hoje, `docker-compose.yml` e os `appsettings.json` dos dois serviços têm a senha do PostgreSQL (`cashflow`/`cashflow`) e a credencial do RabbitMQ (`guest`/`guest`, default de fábrica) hardcoded diretamente nos arquivos versionados.

Um requisito obrigatório do desafio é um README com instruções claras de como rodar localmente — presumivelmente um avaliador clona o repositório e roda `docker compose up`. Qualquer estratégia de secret que exija receber um valor por um canal fora do repositório (Azure Key Vault de verdade, ou `dotnet user-secrets` com valor nunca commitado) quebraria essa experiência de "clonar e rodar", **a menos que o valor em questão não seja de fato sensível**.

Analisado o conteúdo real: nenhuma das duas credenciais atuais é um segredo de verdade. São defaults de ambiente local totalmente descartável, com efeito só dentro da rede isolada dos containers do avaliador — não há confidencialidade real a proteger. O problema que a issue #16 precisa resolver não é "esconder um valor sensível", é **demonstrar o mecanismo correto** (segredo nunca hardcoded em arquivo versionado, sempre injetado via variável de ambiente) sem inventar um problema de transporte seguro que não existe para esses valores específicos.

## Decisão

Dois ambientes, duas estratégias, sem tentar unificar:

- **Local/referência (Docker Compose)**: arquivo `.env` na raiz do repositório, referenciado no `docker-compose.yml` via interpolação (`${POSTGRES_PASSWORD}` etc.). Um `.env.example` é versionado com os mesmos valores placeholder de hoje (`cashflow`/`guest`) — não há necessidade de ocultá-los, não são sensíveis. O `.env` real (cópia do `.env.example`, sem alteração necessária para rodar o desafio) fica no `.gitignore`. O README instrui `cp .env.example .env` como primeiro passo antes de `docker compose up --build`.
- **Arquitetura-alvo (Azure)**: Azure Key Vault, via Key Vault reference no Container App (já documentado em `docs/architecture.md`, seção "Arquitetura alvo (Azure)") — essa parte não muda com esta ADR, só fica formalmente referenciada aqui como o par desta decisão para o ambiente real.

Um comentário no `.env.example` e uma frase no README deixam explícito que os valores ali são placeholders de desenvolvimento local, não segredos reais, para não confundir quem revisar achando que é uma falha de segurança.

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| `dotnet user-secrets` | Guarda o `secrets.json` fora do repositório, no perfil do usuário do host — não é montado automaticamente dentro de um container `docker-compose`. Fazer funcionar exigiria bind mount do `secrets.json` do host para dentro do container, complexidade real sem ganho, já que o valor não é sensível. Continua disponível como opção pessoal para quem rodar via `dotnet run` fora do compose, mas não é o caminho documentado no README. |
| Manter os valores hardcoded direto no `docker-compose.yml`/`appsettings.json` (estado atual) | O mecanismo de injeção (variável de ambiente) já está correto no `docker-compose.yml`, mas o *valor* hardcoded no arquivo versionado mistura as duas coisas — para quem avalia a entrega tecnicamente, isso lê como não separar config de segredo, mesmo o dado sendo inofensivo. |
| Azure Key Vault (ou equivalente real) também na implementação de referência local | Exigiria conta Azure para rodar/avaliar o desafio localmente — quebra o requisito obrigatório de rodar localmente sem dependência de nuvem. Key Vault é o alvo correto em produção, não na referência local. |
| Ferramentas de secret management dedicadas para o ambiente local (Docker secrets, Vault local/HashiCorp, SOPS) | Overengineering para um valor que não é sensível num desafio de avaliação — adicionar essas peças de infraestrutura não protegeria nada que já não esteja adequadamente isolado pela rede de containers descartável do avaliador. |

## Consequências

- `docker-compose.yml` passa a referenciar `${POSTGRES_USER}`, `${POSTGRES_PASSWORD}`, `${RABBITMQ_USER}`, `${RABBITMQ_PASSWORD}` (nomes exatos definidos na implementação) em vez de valores literais.
- `.env.example` é versionado; `.env` está no `.gitignore` (adicionado se ainda não estiver).
- `appsettings.json` dos dois serviços mantêm os mesmos valores de fallback para quem rodar via `dotnet run` fora do container — mesmo raciocínio de "não sensível", sem tratamento adicional.
- Se um segredo real vier a existir no projeto (não é o caso hoje), a mesma estratégia de dois ambientes se aplica: nunca hardcoded, `.env`/variável de ambiente localmente só se o valor real também não for sensível, Key Vault (ou equivalente) sempre que o valor for de fato confidencial.
