-- Instância única de PostgreSQL hospedando os dois bancos lógicos (Lançamentos, Consolidado),
-- com ownership lógico separado entre os serviços — mesmo padrão da arquitetura-alvo Azure
-- (ADR-003). Executado automaticamente pela imagem oficial do Postgres no primeiro start
-- (docker-entrypoint-initdb.d), a partir do banco/usuário definidos em POSTGRES_USER.
CREATE DATABASE lancamentos;
CREATE DATABASE consolidado;
