FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/Knutr.Sdk/Knutr.Sdk.csproj src/Knutr.Sdk/
COPY src/Knutr.Sdk.Hosting/Knutr.Sdk.Hosting.csproj src/Knutr.Sdk.Hosting/
COPY src/Knutr.Plugins.Joke/Knutr.Plugins.Joke.csproj src/Knutr.Plugins.Joke/

RUN dotnet restore src/Knutr.Plugins.Joke/Knutr.Plugins.Joke.csproj

COPY src/Knutr.Sdk/ src/Knutr.Sdk/
COPY src/Knutr.Sdk.Hosting/ src/Knutr.Sdk.Hosting/
COPY src/Knutr.Plugins.Joke/ src/Knutr.Plugins.Joke/

RUN dotnet publish src/Knutr.Plugins.Joke/Knutr.Plugins.Joke.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN addgroup --system knutr && adduser --system --ingroup knutr knutr
USER knutr

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Knutr.Plugins.Joke.dll"]
