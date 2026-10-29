# Contexto de build: raíz del repositorio
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj src/BuildingBlocks/EduMonitor.Common/
COPY src/Services/Academic/EduMonitor.Academic.Api/EduMonitor.Academic.Api.csproj src/Services/Academic/EduMonitor.Academic.Api/
RUN dotnet restore src/Services/Academic/EduMonitor.Academic.Api/EduMonitor.Academic.Api.csproj
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Academic/ src/Services/Academic/
RUN dotnet publish src/Services/Academic/EduMonitor.Academic.Api/EduMonitor.Academic.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EduMonitor.Academic.Api.dll"]
