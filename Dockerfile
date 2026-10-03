# syntax=docker/dockerfile:1
# Dockerfile de producción para EZmatchApi (ASP.NET Core 10 + EF Core 10 + PostgreSQL).
# Contexto de build: raíz del repositorio.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar solo el proyecto primero para aprovechar la caché de NuGet.
COPY backend/EZmatchApi/EZmatchApi.csproj ./backend/EZmatchApi/
RUN dotnet restore ./backend/EZmatchApi/EZmatchApi.csproj

# Copiar el resto del código y publicar.
COPY backend/EZmatchApi/ ./backend/EZmatchApi/
WORKDIR /src/backend/EZmatchApi
RUN dotnet publish EZmatchApi.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Puerto expuesto. Easy Panel descubrirá este puerto o podés mapearlo al 5000.
EXPOSE 5000

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "EZmatchApi.dll"]
