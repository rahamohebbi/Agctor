# Builds the AGCTOR Orleans silo for local Docker deployment.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy project files first (restore needs the Contracts project reference).
COPY AgctorSDK.Orleans.Contracts/AgctorSDK.Orleans.Contracts.csproj AgctorSDK.Orleans.Contracts/
COPY AgctorSDK.OrleansSilo/AgctorSDK.OrleansSilo.csproj AgctorSDK.OrleansSilo/
RUN dotnet restore AgctorSDK.OrleansSilo/AgctorSDK.OrleansSilo.csproj
COPY AgctorSDK.Orleans.Contracts/ AgctorSDK.Orleans.Contracts/
COPY AgctorSDK.OrleansSilo/ AgctorSDK.OrleansSilo/
RUN dotnet publish AgctorSDK.OrleansSilo/AgctorSDK.OrleansSilo.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 30000 11111 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "AgctorSDK.OrleansSilo.dll"]
