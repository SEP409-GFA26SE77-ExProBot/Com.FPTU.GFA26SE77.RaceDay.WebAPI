# RaceDay.Api — build from repo root:
#   docker build -f deploy/Dockerfile -t raceday-api .
# Compose: deploy/docker-compose.yml service `api` (profile: api)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Shared ./src/Shared
COPY src/Modules/Iam ./src/Modules/Iam
COPY src/Modules/Event ./src/Modules/Event
COPY src/Modules/Payment ./src/Modules/Payment
COPY src/Modules/AuditLog ./src/Modules/AuditLog
COPY src/Modules/RaceMonitoring ./src/Modules/RaceMonitoring
COPY src/Hosts/RaceDay.Api ./src/Hosts/RaceDay.Api

RUN dotnet restore src/Hosts/RaceDay.Api/RaceDay.Api.csproj
RUN dotnet publish src/Hosts/RaceDay.Api/RaceDay.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RaceDay.Api.dll"]
