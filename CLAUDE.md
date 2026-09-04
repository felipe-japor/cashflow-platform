# Desafio Arquiteto de Soluções — instruções do projeto

Solução para o desafio técnico de Arquiteto de Soluções (controle de fluxo de caixa: lançamentos + consolidado diário). Ver `docs/` para requisitos completos. Stack: .NET/C#. Ver a seção "Decisões-chave" abaixo para o resumo da arquitetura acordada com Felipe.

**Orçamento de tempo**: 24h no total, teto rígido (até 4 dias úteis de calendário disponível — de até 5 —, no máximo 6h de trabalho focado por dia). Obrigatórios são compromisso fixo; diferenciais são admitidos incrementalmente conforme a velocidade real comprovar que cabem, nunca pré-comprometidos. Toda priorização de escopo (obrigatório > não-funcional > diferencial) deve respeitar essas 24h — ver `poAgent.md`.

## Convenções

- Commits: sempre em pt-BR, claros sobre o que foi comitado, com prefixo [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`, `ci:` etc.) — mensagem descritiva em português depois dos dois pontos.
- "Suba o código" = criar feature branch + Pull Request. Nunca push direto em `main`/`master`.
- **Fechamento de PR (padrão adotado em 2026-09-04, 16:51)**: ao aceitar um PR, Felipe faz rebase da branch de feature sobre `main` e squash merge (não merge commit) — projeto solo, sem outros devs compartilhando as branches, então rebase é seguro; squash colapsa os commits intermediários de uma PR (ex.: correções encadeadas dentro da mesma issue) em um único commit limpo em `main`. A branch remota (e a local) é excluída logo após o aceite. Vale só **daqui para frente** — nenhum merge já feito antes dessa data é retroagido/reescrito.
- **`used-prompts/log.md` nunca justifica um PR próprio (padrão adotado em 2026-09-04, ~17:15)**: é um artefato transversal (o hook atualiza a cada prompt, independente da branch), não uma entrega isolada. Deixar as atualizações pendentes acumularem sem commit até a próxima branch de trabalho real ser aberta, e então subir junto — como parte de uma issue maior, nunca como PR dedicado só para o log.
- Simplicidade acima de tudo (KISS) — a implementação deve ser a mais simples possível que ainda atenda a todos os requisitos (obrigatórios + diferenciais). DRY, SOLID e padrões de projeto aplicados onde fazem sentido de verdade, nunca por decoração.
- Todo prompt do Felipe neste projeto é logado automaticamente em `used-prompts/log.md` via hook — não precisa fazer isso manualmente.
- **Comportamento padrão do orquestrador**: sempre que Felipe mandar uma assertiva descontextualizada ou sem pergunta específica, dar uma opinião de arquiteto experiente (sem preciosismo, com humildade intelectual, priorizando o caminho mais simples, explicando trade-offs), considerando sempre requisitos obrigatórios/diferenciais/não-funcionais — não só confirmar e seguir.
- **Nenhuma issue é aberta no GitHub sem aprovação explícita de Felipe primeiro** — vale para todo agente que possa propor trabalho (hoje: `poAgent`, `secAgent`, `infraAgent`), sempre. Apresentar a lista proposta, esperar aprovação, só então `gh issue create`.

## Decisões-chave já acordadas (resumo)

- **Padrão**: dois serviços (Lançamentos = write model/fonte da verdade; Consolidado = read model), integração assíncrona via evento — nunca chamada síncrona bloqueante entre eles (requisito não-funcional: Lançamentos não pode cair se Consolidado cair).
- **Outbox transacional** no serviço de Lançamentos, para publicar evento com atomicidade em relação à escrita.
- **Banco**: PostgreSQL (motivo: sem custo de licença, roda idêntico local/Azure, bom encaixe técnico — JSONB pro outbox, window functions pro agregado diário). SQL Server só entraria se soubermos que a empresa entrevistadora usa isso como padrão (não sabemos). Lançamentos e Consolidado usam a **mesma instância** de PostgreSQL, com ownership lógico separado (bancos lógicos distintos) — reduz complexidade operacional dentro do prazo. Trade-off (contenção de recursos compartilhados sob carga) e evolução recomendada (instâncias separadas) documentados no ADR-003.
- **Portabilidade cloud**: interfaces + Factory na borda de infraestrutura de **mensageria** (RabbitMQ → Azure Service Bus, uso real e imediato) — implementação de referência roda 100% local via Docker Compose (RabbitMQ, Postgres, tudo OSS), arquitetura alvo documentada mapeia pra Azure gerenciado (Service Bus, Postgres Flexible Server, Container Apps). Troca é config/Factory, não reescrita. **Cache não está no escopo comprometido inicial** — o SLA de 50 req/s é atendido pelo Postgres com índice em `Data` dada a baixa cardinalidade do consolidado diário; só seria adicionado (com a mesma estratégia de interface+Factory, se necessário) com evidência real de teste de carga (issue #20). Ver ADR-003.
- **Observabilidade**: OpenTelemetry desde o início (vendor-neutral), não SDK de vendor específico — funciona igual local (console/Jaeger) e Azure (Monitor).
- **Compute alvo**: Azure Container Apps, não App Service — os serviços são workers headless (sem endpoint HTTP nativo), App Service exigiria listener HTTP artificial.

**ADRs não ficam represadas até a issue #4.** Apesar de a redação formal dos 5 ADRs estar isolada como issue própria, qualquer decisão arquitetural nova ou revisada durante o processo atualiza o ADR correspondente (ou cria um novo) no momento em que a decisão é tomada — não espera a issue #4 ser formalmente trabalhada. A issue #4 cobre o que sobrar de redação pura no fim; decisão registrada tarde é decisão que se perde ou diverge do código.

**Como decidir entre atualizar um ADR existente ou criar um novo — depende da fase:**
- **Enquanto ainda estivermos na Fase 1 (planejamento)**: mudança ou redefinição de uma decisão já registrada **atualiza o ADR existente in-place** — a arquitetura ainda está convergindo, não faz sentido acumular ADRs conflitantes de um período em que a decisão nem tinha estabilizado.
- **A partir da Fase 2 em diante (implementação, qualidade, validação)**: mudança ou redefinição de uma decisão já registrada **cria um ADR novo**, que referencia e supera o anterior — não edita o antigo. Registro histórico imutável: decisão tomada durante a execução é um evento que aconteceu, editar o ADR anterior apagaria esse rastro. Só editar um ADR já existente nessa fase se Felipe orientar explicitamente o contrário para aquele caso específico.

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
