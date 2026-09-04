# Desafio Arquiteto de Soluções — Controle de Fluxo de Caixa

> Status: rascunho inicial (estrutura). Conteúdo a ser desenvolvido.

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
docker compose up --build
```

Sobe Postgres (bancos lógicos `lancamentos` e `consolidado` na mesma instância — ADR-003), RabbitMQ (broker local de referência — ADR-004/ADR-005) e os dois serviços:

- Lançamentos: http://localhost:5101
- Consolidado: http://localhost:5102
- RabbitMQ management UI: http://localhost:15672 (guest/guest)

> Nesta etapa (scaffolding — issue #6) os serviços só expõem um endpoint raiz de verificação; os endpoints de negócio (registrar/consultar lançamentos, consultar saldo consolidado) chegam nas próximas issues. Instruções completas de validação local ponta a ponta são finalizadas na issue #22.

## Estrutura do repositório

```
/src              código-fonte
/tests            testes automatizados
/docs             documentação de arquitetura
/used-prompts     log de prompts usados no desenvolvimento assistido por IA
docker-compose.yml
```
