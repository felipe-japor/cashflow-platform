# Desafio Arquiteto de Soluções — Controle de Fluxo de Caixa

> Status: obrigatórios da Fase 2 (implementação) em andamento — cadastro/consulta de lançamentos, outbox/publisher/DLQ/consumer, cálculo/consulta do consolidado, health checks e gestão de secrets já entregues. Ver `docs/adr/` para as decisões arquiteturais e `used-prompts/log.md` para o histórico completo de como a IA foi conduzida.

Solução para o desafio técnico de Arquiteto de Soluções: controle de lançamentos (débito/crédito) e consolidado diário de saldo para um comerciante.

## Documentação

- [Mapeamento de domínios e capacidades de negócio](docs/domain-mapping.md)
- [Requisitos funcionais e não funcionais](docs/requirements.md)
- [Arquitetura](docs/architecture.md)
- [Arquitetura de transição](docs/transition-architecture.md)
- [Estimativa de custos](docs/cost-estimate.md)
- [Observabilidade](docs/observability.md)
- [Segurança de integração](docs/integration-security.md)
- [ADRs](docs/adr/)
- [Diagramas](docs/diagrams/)
- [Prompts utilizados no desenvolvimento](used-prompts/log.md)

## Como rodar localmente

Pré-requisitos: Docker e Docker Compose.

```bash
cp .env.example .env
docker compose down --remove-orphans
docker compose up --build
```

> O `down --remove-orphans` antes do `up` garante uma rede/estado limpos mesmo que uma execução anterior tenha ficado de pé — evita falhas de resolução de DNS entre os containers (`Name or service not known`) por rede órfã de uma sessão anterior. Sempre rode nessa ordem, mesmo na primeira vez.

> Os valores em `.env.example` são placeholders de desenvolvimento local (`cashflow`/`cashflow`, `guest`/`guest`), não segredos reais — ver ADR-006. `.env` (cópia local, ignorada pelo Git) é a fonte das credenciais injetadas no `docker-compose.yml`; não é preciso alterá-lo para rodar o desafio.

Sobe Postgres (bancos lógicos `lancamentos` e `consolidado` na mesma instância — ADR-003), RabbitMQ (broker local de referência — ADR-004/ADR-005) e os dois serviços:

- Lançamentos: http://localhost:5101
- Consolidado: http://localhost:5102
- RabbitMQ management UI: http://localhost:15672 (guest/guest)

Endpoints disponíveis hoje:

- **Lançamentos** (`:5101`): `POST /lancamentos` (registrar débito/crédito), `GET /lancamentos?dataInicial=&dataFinal=` (consultar por período), `GET /health/live`, `GET /health/ready`.
- **Consolidado** (`:5102`): `GET /consolidado/{data}` (posição do dia — 404 se ainda não houver lançamento processado para a data), `GET /consolidado?dataInicial=&dataFinal=` (consulta por período), `GET /health/live`, `GET /health/ready`.

> Validação local ponta a ponta (subida completa + smoke test do fluxo principal) está coberta pelo teste automatizado em `tests/EndToEnd.Tests` (issue #18/#22). Revisão final em ambiente limpo (clone do zero) fica para a issue #24.

## Estrutura do repositório

```
/src              código-fonte
/tests            testes automatizados
/docs             documentação de arquitetura
/used-prompts     log de prompts usados no desenvolvimento assistido por IA
docker-compose.yml
```
