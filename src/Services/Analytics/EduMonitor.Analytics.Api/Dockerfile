# Contexto de build: raíz del repositorio
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BuildingBlocks/EduMonitor.Common/EduMonitor.Common.csproj src/BuildingBlocks/EduMonitor.Common/
COPY src/Services/Analytics/EduMonitor.Analytics.Api/EduMonitor.Analytics.Api.csproj src/Services/Analytics/EduMonitor.Analytics.Api/
RUN dotnet restore src/Services/Analytics/EduMonitor.Analytics.Api/EduMonitor.Analytics.Api.csproj
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Analytics/ src/Services/Analytics/
RUN dotnet publish src/Services/Analytics/EduMonitor.Analytics.Api/EduMonitor.Analytics.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# QuestPDF (reporte PDF, HU-16) necesita fuentes y FreeType en la imagen runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EduMonitor.Analytics.Api.dll"]
