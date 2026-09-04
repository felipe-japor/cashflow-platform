# Diagramas

Cada diagrama é entregue em dois formatos: `.png` (visualização direta) e `.xml` (fonte editável, importável no [draw.io](https://app.diagrams.net/), para reaproveitamento futuro).

| Diagrama | Descrição |
|---|---|
| `c4-context.png` / `.xml` | C4 - Nível de Contexto |
| `c4-container.png` / `.xml` | C4 - Nível de Container |
| `sequence-registrar-lancamento.png` / `.xml` | Sequência: registrar lançamento → outbox → broker → atualização do consolidado |

> Status: criados (issue #5). PNG gerado a partir da mesma fonte de dados do XML (script utilitário, não versionado — os artefatos finais são o PNG e o XML), garantindo consistência entre os dois formatos. XML testado como importável no draw.io.
