# Build context: raiz do repositório (docker-compose.yml). Ver docker-compose.yml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CashFlowPlatform.sln ./
COPY src/Lancamentos/Lancamentos.Api/Lancamentos.Api.csproj src/Lancamentos/Lancamentos.Api/
COPY src/Lancamentos/Lancamentos.Application/Lancamentos.Application.csproj src/Lancamentos/Lancamentos.Application/
COPY src/Lancamentos/Lancamentos.Domain/Lancamentos.Domain.csproj src/Lancamentos/Lancamentos.Domain/
COPY src/Lancamentos/Lancamentos.Infrastructure/Lancamentos.Infrastructure.csproj src/Lancamentos/Lancamentos.Infrastructure/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
RUN dotnet restore src/Lancamentos/Lancamentos.Api/Lancamentos.Api.csproj

COPY src/Lancamentos/ src/Lancamentos/
COPY src/Shared/ src/Shared/
RUN dotnet publish src/Lancamentos/Lancamentos.Api/Lancamentos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Lancamentos.Api.dll"]
