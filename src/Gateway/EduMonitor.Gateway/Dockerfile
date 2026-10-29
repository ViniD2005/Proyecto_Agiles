# Contexto de build: raíz del repositorio
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj src/BuildingBlocks/EduMonitor.Common/
COPY src/Gateway/EduMonitor.Gateway/EduMonitor.Gateway.csproj src/Gateway/EduMonitor.Gateway/
RUN dotnet restore src/Gateway/EduMonitor.Gateway/EduMonitor.Gateway.csproj
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/EduMonitor.Gateway/EduMonitor.Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EduMonitor.Gateway.dll"]
