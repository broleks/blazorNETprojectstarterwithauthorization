# syntax=docker/dockerfile:1
# Multi-stage build: the SDK image builds and publishes, the small ASP.NET Core runtime image runs the app.
# Build from the repository root:  docker build -f src/Starter.Identity/Dockerfile -t starter-identity .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/

# The NuGet cache mount keeps rebuilds fast without baking packages into a layer. --locked-mode fails the
# build if packages.lock.json does not match, so the image contains exactly the reviewed dependency versions.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Starter.Identity/Starter.Identity.csproj --locked-mode
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Starter.Identity/Starter.Identity.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Runs as the non-root "app" user that the official .NET images provide.
USER $APP_UID
# Plain HTTP on 8080 behind a TLS-terminating proxy (Azure App Service). Docker Compose overrides
# ASPNETCORE_URLS to serve HTTPS directly with the development certificate.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Starter.Identity.dll"]
