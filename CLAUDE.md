# Desafio Arquiteto de Soluções — instruções do projeto

Solução para o desafio técnico de Arquiteto de Soluções (controle de fluxo de caixa: lançamentos + consolidado diário). Ver `docs/` para requisitos completos. Stack: .NET/C#. Ver a seção "Decisões-chave" abaixo para o resumo da arquitetura acordada com Felipe.

**Orçamento de tempo**: 24h no total, teto rígido (até 4 dias úteis de calendário disponível — de até 5 —, no máximo 6h de trabalho focado por dia). Obrigatórios são compromisso fixo; diferenciais são admitidos incrementalmente conforme a velocidade real comprovar que cabem, nunca pré-comprometidos. Toda priorização de escopo (obrigatório > não-funcional > diferencial) deve respeitar essas 24h — ver `poAgent.md`.

## Convenções

- Commits: sempre em pt-BR, claros sobre o que foi comitado, com prefixo [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`, `ci:` etc.) — mensagem descritiva em português depois dos dois pontos.
- "Suba o código" = criar feature branch + Pull Request. Nunca push direto em `main`/`master`.
- Simplicidade acima de tudo (KISS) — a implementação deve ser a mais simples possível que ainda atenda a todos os requisitos (obrigatórios + diferenciais). DRY, SOLID e padrões de projeto aplicados onde fazem sentido de verdade, nunca por decoração.
- Todo prompt do Felipe neste projeto é logado automaticamente em `used-prompts/log.md` via hook — não precisa fazer isso manualmente.
- **Comportamento padrão do orquestrador**: sempre que Felipe mandar uma assertiva descontextualizada ou sem pergunta específica, dar uma opinião de arquiteto experiente (sem preciosismo, com humildade intelectual, priorizando o caminho mais simples, explicando trade-offs), considerando sempre requisitos obrigatórios/diferenciais/não-funcionais — não só confirmar e seguir.
- **Nenhuma issue é aberta no GitHub sem aprovação explícita de Felipe primeiro** — vale para todo agente que possa propor trabalho (hoje: `poAgent`, `secAgent`, `infraAgent`), sempre. Apresentar a lista proposta, esperar aprovação, só então `gh issue create`.

## Decisões-chave já acordadas (resumo)

- **Padrão**: dois serviços (Lançamentos = write model/fonte da verdade; Consolidado = read model), integração assíncrona via evento — nunca chamada síncrona bloqueante entre eles (requisito não-funcional: Lançamentos não pode cair se Consolidado cair).
- **Outbox transacional** no serviço de Lançamentos, para publicar evento com atomicidade em relação à escrita.
- **Banco**: PostgreSQL (motivo: sem custo de licença, roda idêntico local/Azure, bom encaixe técnico — JSONB pro outbox, window functions pro agregado diário). SQL Server só entraria se soubermos que a empresa entrevistadora usa isso como padrão (não sabemos).
- **Portabilidade cloud**: interfaces + Factory na borda de infraestrutura (mensageria, cache) — implementação de referência roda 100% local via Docker Compose (RabbitMQ, Redis, Postgres, tudo OSS), arquitetura alvo documentada mapeia pra Azure gerenciado (Service Bus, Cache for Redis, Postgres Flexible Server, Container Apps). Troca é config/Factory, não reescrita.
- **Observabilidade**: OpenTelemetry desde o início (vendor-neutral), não SDK de vendor específico — funciona igual local (console/Jaeger) e Azure (Monitor).
- **Compute alvo**: Azure Container Apps, não App Service — os serviços são workers headless (sem endpoint HTTP nativo), App Service exigiria listener HTTP artificial.

**ADRs não ficam represadas até a issue #4.** Apesar de a redação formal dos 5 ADRs estar isolada como issue própria, qualquer decisão arquitetural nova ou revisada durante o processo atualiza o ADR correspondente (ou cria um novo) no momento em que a decisão é tomada — não espera a issue #4 ser formalmente trabalhada. A issue #4 cobre o que sobrar de redação pura no fim; decisão registrada tarde é decisão que se perde ou diverge do código.

## Time de agentes (`.claude/agents/`)

| Agente | Arquivo | Papel |
|---|---|---|
| Product Owner | `poAgent.md` | Orquestra o fluxo de issues e a progressão de entrega; prioriza ruthlessly dentro do orçamento de tempo; só sugere o que é crucial |
| Dev Sênior | `devSrAgent.md` | Implementa issues (código + testes unitários), defende Clean Code/DRY/SOLID/KISS, abre PR |
| Tester | `testerAgent.md` | Valida PR do Dev Sênior; mantém cobertura de testes unitários na faixa 70%-80% (coverlet), priorizando partes críticas |
| Arquiteto Adjunto | `AuxArchitect.md` | Consultivo — auxilia Felipe em decisões de arquitetura; primeira parada de dúvidas do Dev Sênior e do PO |
| Security Specialist | `secAgent.md` | Varredura de segurança sob demanda — código + documentos arquiteturais |
| Infra Agent | `infraAgent.md` | Varredura de infraestrutura sob demanda — código + documentos arquiteturais |

## Workflow de desenvolvimento (padrão, sempre que houver issue/tarefa a implementar)

1. **Dev Sênior** implementa a issue (código + testes unitários) num branch próprio, e abre PR no GitHub (`gh pr create`, referenciando a issue, label do agente).
2. Se o Dev Sênior tiver dúvida real de design durante a implementação, ele consulta o **Arquiteto Adjunto** antes de chegar a Felipe. Se depois do debate entre os dois a dúvida persistir, aí sim escalar para Felipe.
3. **Tester** valida o PR: roda a suíte completa, mede cobertura real via coverlet (`dotnet test --collect:"XPlat Code Coverage"`).
   - Se a cobertura está na faixa 70%-80% (ou acima, sem necessidade de perseguir 100%) e os testes existentes são de qualidade: **sinaliza para Felipe avaliar o PR** — não mexe em nada.
   - Se está abaixo de 70% (ou os testes existentes são frágeis/triviais): o Tester **sobe as correções e testes unitários adicionais no mesmo branch** (sem abrir PR novo), priorizando cobertura das partes críticas do domínio, e **comenta no PR e na issue relacionada** explicando o que foi ajustado — só então sinaliza para Felipe.
4. Felipe revisa e decide sobre o merge.

## Varreduras sob demanda

- **"executar a varredura de segurança"** → despachar o **Security Specialist**, que avalia código + documentos arquiteturais (`docs/`) e apresenta relatório de issues encontradas, ordenado por severidade. Não cria issues no GitHub sem aprovação explícita de Felipe.
- **"executar a varredura de infra"** → despachar o **Infra Agent**, mesmo formato de relatório, focado em infraestrutura (Docker, arquitetura alvo Azure, IaC, custo, resiliência/escalabilidade de deploy). Não cria issues no GitHub sem aprovação explícita de Felipe.
