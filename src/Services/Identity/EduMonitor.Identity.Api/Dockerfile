# Contexto de build: raíz del repositorio
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj src/BuildingBlocks/EduMonitor.Common/
COPY src/Services/Identity/EduMonitor.Identity.Api/EduMonitor.Identity.Api.csproj src/Services/Identity/EduMonitor.Identity.Api/
RUN dotnet restore src/Services/Identity/EduMonitor.Identity.Api/EduMonitor.Identity.Api.csproj
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Identity/ src/Services/Identity/
RUN dotnet publish src/Services/Identity/EduMonitor.Identity.Api/EduMonitor.Identity.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EduMonitor.Identity.Api.dll"]
