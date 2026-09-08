# Harness de IA

> Como a IA foi conduzida neste projeto: não "peça e receba código", mas um processo com papéis, limites e pontos de decisão humana definidos. Este documento descreve a estrutura — o "como" — como complemento a `used-prompts/log.md`, que registra o "o quê" (cada prompt real enviado). A estrutura abaixo foi definida no primeiro dia do projeto (ver `used-prompts/log.md`, entradas 001-018), antes mesmo de o hook de log de prompts existir — o hook automatizou um registro que já fazia parte do desenho original, não o inverso.

**Objetivo**: controle de qualidade e de escopo. Qualidade, porque cada papel tem uma responsabilidade estreita e uma saída verificável (cobertura de teste real, não alegada; decisão registrada, não implícita). Escopo, porque o orçamento de tempo do desafio (24h, teto rígido) só se sustenta se a IA não expandir trabalho por conta própria — daí os guardrails abaixo.

## Componentes

### 1. Contexto persistente

`CLAUDE.md`, na raiz do repositório, é lido automaticamente no início de toda sessão — convenções de commit, decisões-chave já acordadas, workflow padrão de desenvolvimento e a lista de agentes. Funciona como memória de longo prazo do projeto: qualquer decisão arquitetural relevante é escrita ali (ou num ADR referenciado por ele) assim que tomada, não só na cabeça de quem conduziu a sessão. Isso permite retomar o projeto em qualquer momento, ou trocar de sessão, sem perder o histórico de decisões.

### 2. Agentes

Seis papéis especializados, cada um com escopo de ferramentas e responsabilidade definidos em `.claude/agents/*.md`:

| Agente | Papel | Decide arquitetura? |
|---|---|---|
| `poAgent` | Orquestra o backlog de issues, prioriza dentro do orçamento de 24h | Não — só escopo/sequenciamento |
| `devSrAgent` | Implementa issues (código + testes unitários), abre PR | Não — segue decisão já tomada |
| `testerAgent` | Valida PR: roda suíte, mede cobertura real (coverlet) | Não |
| `AuxArchitect` | Consultivo — decisão de design embasada, primeira parada de dúvida do Dev/PO | Sim, no nível de recomendação |
| `secAgent` | Varredura de segurança sob demanda, relatório por severidade | Não — só recomenda |
| `infraAgent` | Varredura de infraestrutura sob demanda (deploy, custo, resiliência) | Não — só recomenda |

Nenhum agente decide unilateralmente uma mudança de arquitetura ou de escopo — todos alimentam uma decisão que passa por Felipe (seção 5).

### 3. Orquestração dos agentes

O agente orquestrador (esta sessão) é o único ponto de entrada de Felipe. Ele interpreta o pedido, decide qual agente especializado despachar (ou nenhum, se a tarefa é direta), e integra o resultado de volta à conversa — os agentes especializados não conversam entre si sem passar pelo orquestrador. O fluxo padrão de uma issue (`CLAUDE.md`, seção "Workflow de desenvolvimento"): `devSrAgent` implementa → dúvida real de design vai a `AuxArchitect` antes de chegar a Felipe → `testerAgent` valida o PR → Felipe decide o merge. Varreduras de segurança/infra são sob demanda, fora desse fluxo padrão.

### 4. Guardrails

Restrições fixas que a IA nunca ultrapassa por iniciativa própria, registradas em `CLAUDE.md`:

- Nenhuma issue é aberta no GitHub sem aprovação explícita de Felipe.
- Nenhum PR é aberto sem instrução explícita ("suba o código") — nunca push direto em `main`.
- Nenhum ADR novo é criado durante a fase de validação de POC (arquitetura ainda em evolução) sem instrução explícita — decisão registrada cedo demais é decisão que pode divergir do código.
- Simplicidade acima de tudo (KISS) — a implementação mais simples que atenda a todos os requisitos, nunca a mais "impressionante".
- Orçamento de tempo de 24h é teto rígido — diferenciais são admitidos incrementalmente, nunca pré-comprometidos (ver `poAgent.md`).

### 5. Escalonamento do processo decisório

Dúvida de design segue uma escada fixa: `devSrAgent`/`poAgent` → `AuxArchitect` → Felipe, só subindo o degrau se o anterior não resolver. Quando dois agentes especialistas dão recomendações genuinamente conflitantes (ex.: `AuxArchitect` e `secAgent` sobre estrutura de ADR de segurança), o orquestrador **apresenta o conflito de forma transparente a Felipe**, em vez de resolver unilateralmente por conta própria — a decisão final sobre trade-off arquitetural é sempre humana.

### 6. Feedback automatizado

O `testerAgent` fecha um loop de qualidade sem intervenção humana a cada PR: mede cobertura real via coverlet, e só sinaliza Felipe para revisão se a cobertura já está na faixa 70%-80% com testes de qualidade. Abaixo disso, ele mesmo sobe correções e testes adicionais no mesmo branch antes de sinalizar — feedback automatizado, mas com o resultado sempre visível a Felipe antes do merge.

### 7. Rastreabilidade de IA

Dois mecanismos complementares:

- **`used-prompts/log.md`**: cada prompt real enviado por Felipe neste projeto é registrado automaticamente por um hook `UserPromptSubmit` (`.claude/hooks/log_prompt.py`), append-only, numerado sequencialmente — permite reconstituir exatamente como a IA foi conduzida, prompt a prompt.
- **"Diretriz de implementação" nas issues**: para decisões não triviais, o orquestrador escreve a decisão (com o racional e as considerações dos agentes consultados) diretamente no corpo da issue do GitHub, via `gh issue edit`, **antes** de qualquer implementação começar — a decisão fica registrada onde o trabalho será feito, não só na conversa.

### 8. Human in the loop

Felipe aprova, em algum ponto, toda decisão que importa: criação de issue, abertura de PR, merge, criação de ADR, resolução de conflito entre agentes, corte de escopo. Nenhum agente (incluindo o orquestrador) tem autoridade para fechar essas decisões sozinho — o papel da IA é preparar insumos para a decisão (pesquisa e recomendações), não tomá-la.
