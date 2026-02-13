# Knutr Core - Slack bot host
# Build: docker build -t knutr-core .
# Run:   docker run -p 7071:7071 knutr-core

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY Directory.Build.props .
COPY Knutr.sln .
COPY src/Knutr.Abstractions/Knutr.Abstractions.csproj src/Knutr.Abstractions/
COPY src/Knutr.Core/Knutr.Core.csproj src/Knutr.Core/
COPY src/Knutr.Sdk/Knutr.Sdk.csproj src/Knutr.Sdk/
COPY src/Knutr.Sdk.Hosting/Knutr.Sdk.Hosting.csproj src/Knutr.Sdk.Hosting/
COPY src/Knutr.Infrastructure/Knutr.Infrastructure.csproj src/Knutr.Infrastructure/
COPY src/Knutr.Adapters.Slack/Knutr.Adapters.Slack.csproj src/Knutr.Adapters.Slack/
COPY src/Knutr.Hosting/Hosting.csproj src/Knutr.Hosting/
COPY src/Knutr.Plugins.PingPong/Knutr.Plugins.PingPong.csproj src/Knutr.Plugins.PingPong/

RUN dotnet restore src/Knutr.Hosting/Hosting.csproj

# Copy everything and publish
COPY src/ src/
RUN dotnet publish src/Knutr.Hosting/Hosting.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN addgroup --system knutr && adduser --system --ingroup knutr knutr
USER knutr

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:7071
EXPOSE 7071

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:7071/health || exit 1

ENTRYPOINT ["dotnet", "Knutr.Hosting.dll"]
