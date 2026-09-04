# ADR-001: Arquitetura orientada a eventos entre os serviços de Lançamentos e Consolidado

> Status: aceito.

## Contexto

O desafio define dois domínios com taxas de mudança e perfis de carga bem diferentes (ver `docs/domain-mapping.md`): Lançamentos é write-heavy (RF01/RF02, fonte da verdade do fluxo de caixa) e Consolidado é read-heavy (RF03/RF04, read model de saldo diário). Além da diferença de perfil, o próprio desafio impõe um requisito não funcional explícito e literal: o serviço de Lançamentos não pode ficar indisponível caso o serviço de Consolidado caia (NFR01). Some-se a isso um SLA de carga concreto no Consolidado — 50 requisições por segundo em dias de pico, com no máximo 5% de perda (NFR02).

Esses três fatores em conjunto — domínios com responsabilidades e cadência de mudança distintas, exigência explícita de isolamento de disponibilidade e perfis de carga assimétricos — definem o problema real que a integração entre os dois serviços precisa resolver: como Lançamentos comunica ao Consolidado que um novo lançamento aconteceu, sem que a saúde ou a carga de um dos dois serviços comprometa o outro.

## Decisão

A integração entre Lançamentos e Consolidado é **100% assíncrona via evento de domínio**, nunca por chamada síncrona bloqueante em nenhuma das duas direções. O serviço de Lançamentos publica o evento `LancamentoRegistrado` (contrato detalhado em `docs/domain-mapping.md`) sempre que um lançamento é registrado com sucesso; o serviço de Consolidado consome esse evento e atualiza seu read model (`DailyConsolidation`) de forma incremental.

Lançamentos é o produtor: não sabe que o Consolidado existe, não depende dele, não faz nenhuma chamada para ele. Publica o evento na própria outbox (ADR-002) e segue. Consolidado é o consumidor: não consulta o serviço de Lançamentos para montar seu read model, só reage ao evento consumido da fila. Essa relação é estritamente upstream (Lançamentos) → downstream (Consolidado), nunca o inverso.

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| Monólito modular (os dois domínios como módulos de um único processo/deploy) | Não satisfaz o isolamento de disponibilidade exigido por NFR01 com a mesma força: mesmo processo implica mesmo blast radius — uma falha ou sobrecarga em código do módulo Consolidado (por exemplo, sob os 50 req/s de NFR02) pode degradar ou derrubar o processo inteiro, inclusive o caminho de escrita de Lançamentos. Também não permite escalar os dois lados de forma independente, apesar de terem perfis de carga (write-heavy vs. read-heavy) claramente diferentes. |
| Chamada síncrona (REST) do Consolidado para o Lançamentos sob demanda, sem persistência própria no Consolidado | Descartada por violar diretamente NFR01: se o Consolidado fizesse fan-out síncrono para o Lançamentos a cada consulta, uma indisponibilidade ou lentidão do Consolidado (por exemplo, sob os 50 req/s de NFR02) propagaria pressão de volta para o Lançamentos, e uma indisponibilidade do Lançamentos deixaria o Consolidado sem dado algum para responder. A direção inversa (Lançamentos chamando Consolidado de forma síncrona ao registrar) é ainda pior, pois amarraria a operação central do sistema (RF01) à disponibilidade de um serviço secundário. |

## Consequências

- Consistência eventual entre os dois serviços é uma característica aceita do desenho, não uma falha a ser eliminada — documentada como tal em NFR03/RF04 (`docs/requirements.md`): o saldo consolidado pode apresentar defasagem de segundos em relação ao lançamento mais recente, em troca do isolamento de disponibilidade garantido por NFR01.
- O Consolidado precisa manter seu próprio read model materializado (`DailyConsolidation`), atualizado incrementalmente a partir dos eventos — não pode fazer `JOIN` direto nas tabelas de Lançamentos para montar a resposta de RF04, mesmo quando os dois bancos lógicos compartilham a mesma instância física de PostgreSQL (reforça o ownership lógico separado decidido no ADR-003).
- A entrega do evento é at-least-once (garantia típica de broker de mensageria), o que exige consumo idempotente no Consolidado (NFR04) — tratado à parte via inbox pattern (`docs/domain-mapping.md`), fora do escopo desta ADR.
- A publicação do evento precisa de atomicidade em relação à escrita do lançamento para não se perder em caso de falha do processo entre o commit e a publicação — resolvido pelo padrão Transactional Outbox, tratado na ADR-002.
