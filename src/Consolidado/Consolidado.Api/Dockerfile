# Build context: raiz do repositório (docker-compose.yml). Ver docker-compose.yml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CashFlowPlatform.sln ./
COPY src/Consolidado/Consolidado.Api/Consolidado.Api.csproj src/Consolidado/Consolidado.Api/
COPY src/Consolidado/Consolidado.Application/Consolidado.Application.csproj src/Consolidado/Consolidado.Application/
COPY src/Consolidado/Consolidado.Domain/Consolidado.Domain.csproj src/Consolidado/Consolidado.Domain/
COPY src/Consolidado/Consolidado.Infrastructure/Consolidado.Infrastructure.csproj src/Consolidado/Consolidado.Infrastructure/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
RUN dotnet restore src/Consolidado/Consolidado.Api/Consolidado.Api.csproj

COPY src/Consolidado/ src/Consolidado/
COPY src/Shared/ src/Shared/
RUN dotnet publish src/Consolidado/Consolidado.Api/Consolidado.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Consolidado.Api.dll"]
