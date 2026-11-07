# syntax=docker/dockerfile:1
# Multi-stage build: the SDK image builds and publishes, the small ASP.NET Core runtime image runs the app.
# Build from the repository root:  docker build -f src/Starter.Api/Dockerfile -t starter-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, using only project and lock files, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Starter.Contracts/Starter.Contracts.csproj src/Starter.Contracts/packages.lock.json src/Starter.Contracts/
COPY src/Starter.Api/Starter.Api.csproj src/Starter.Api/packages.lock.json src/Starter.Api/
RUN dotnet restore src/Starter.Api/Starter.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/Starter.Api/Starter.Api.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Runs as the non-root "app" user that the official .NET images provide.
USER $APP_UID
# Plain HTTP on 8080 behind a TLS-terminating proxy (Azure App Service). Docker Compose overrides
# ASPNETCORE_URLS to serve HTTPS directly with the development certificate.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Starter.Api.dll"]
