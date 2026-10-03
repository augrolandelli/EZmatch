# EZmatch — Monorepo

Reservas de canchas atendidas por IA en el WhatsApp del club + panel web del club.
La especificación completa está en **[ESPECIFICACION_EZMATCH.md](./ESPECIFICACION_EZMATCH.md)** — leerla antes de implementar cualquier feature.

## Estructura (objetivo)

```
├── backend/EZmatchApi/        # API REST — ASP.NET Core 10 + EF Core 10 (PostgreSQL / Npgsql)
├── backend/EZmatchApi.Tests/  # xUnit + Testcontainers (Postgres real)
├── frontend/                  # Panel del club — React 19 + TS + Vite + Tailwind v4
├── n8n/                       # Exports de workflows del bot (sin credenciales)
├── docker-compose.yml         # Postgres para desarrollo local
├── docker-compose.prod.yml    # api + postgres para Easypanel
└── ESPECIFICACION_EZMATCH.md
```

## Comandos

### Base de datos local
```bash
docker compose up -d     # Postgres 17 en localhost:5434 (5432 y 5433 ya están ocupados en esta máquina)
```
Cadena de conexión en `backend/EZmatchApi/appsettings.Development.json` (copiar del `.example`).

### Backend
```bash
dotnet build EZmatch.slnx
dotnet run --project backend/EZmatchApi --launch-profile http   # http://localhost:5219
dotnet test EZmatch.slnx        # requiere Docker corriendo (Testcontainers)
dotnet ef migrations add <Nombre> --project backend/EZmatchApi
```
- La API aplica migraciones al arrancar (Development y Production).
- Health: `GET /api/health` (incluye estado de la base). OpenAPI (dev): `/openapi/v1.json`.

### Frontend (`frontend/`)
```bash
npm install
npm run dev     # http://localhost:5173 (proxea /api → http://localhost:5219)
npm run build
npm run lint
```

### Producción
`Dockerfile` (raíz) + `docker-compose.prod.yml` (api + postgres) en Easypanel; variables en `.env` (ver `.env.example`).

## Convenciones

- Código en inglés; UI, mensajes del bot y documentación en español.
- Backend en capas como CalisApp: `Controllers/` finos → `Services/` (reglas) → `Data/`; `Models/`, `Dtos/`.
  Nunca exponer entidades EF. Errores con `AppException` + `GlobalExceptionHandler`.
- **La API es la única fuente de verdad** de disponibilidad, precios y reglas. n8n solo orquesta la conversación.
- Instantes en UTC (`timestamptz`); horas de grilla en hora local del club (`TimeOnly`).
- La no superposición de reservas la garantiza el constraint de exclusión de Postgres (ver spec §5.3).
- Multi-tenant: todo dato de negocio lleva `ClubId`; el panel filtra por el club del JWT.
- Ningún secreto en el repo (`.mcp.json`, `.env`, `appsettings.Development.json` están en `.gitignore`).

## Roadmap

Ver §11 de la especificación.
