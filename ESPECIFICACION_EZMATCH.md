# EZmatch — Especificación Funcional y Técnica

> Reservas de canchas (pádel, fútbol, tenis…) atendidas por IA en el WhatsApp del club,
> con un panel web donde el club ve y gestiona su agenda.

**Estado:** borrador v0.1 — 2026-10-03

## Tabla de contenidos

1. [Visión general](#1-visión-general)
2. [Actores y modelo de negocio](#2-actores-y-modelo-de-negocio)
3. [Reglas de negocio](#3-reglas-de-negocio)
4. [Módulos funcionales](#4-módulos-funcionales)
5. [Modelo de datos](#5-modelo-de-datos)
6. [Contrato de API](#6-contrato-de-api)
7. [Bot de WhatsApp (n8n + Chatwoot)](#7-bot-de-whatsapp-n8n--chatwoot)
8. [Stack y arquitectura](#8-stack-y-arquitectura)
9. [Infraestructura y deploy](#9-infraestructura-y-deploy)
10. [Seguridad](#10-seguridad)
11. [Roadmap](#11-roadmap)
12. [Decisiones abiertas](#12-decisiones-abiertas)

---

## 1. Visión general

Hoy los clubes toman turnos por WhatsApp a mano: el encargado contesta todo el día
"¿tenés algo hoy a las 21?". EZmatch pone un agente de IA en **el mismo número de WhatsApp
del club**: el jugador escribe como siempre, sin bajarse ninguna app, y el bot consulta
disponibilidad, reserva y cancela. El club ve todo en un panel web y puede cargar
reservas manuales (teléfono, mostrador).

### Componentes del sistema

```
WhatsApp (Meta Cloud API) → Chatwoot (un inbox por club)
                                 │ webhook message_created
                                 ▼
                           n8n — Agente IA ──tools HTTP──► EZmatch API (.NET 10)
                                 │                            ▲          │
                                 └── responde vía Chatwoot    │      PostgreSQL
                                                     Panel web del club (React)
```

**Principio rector:** la API es la única fuente de verdad. La IA nunca decide
disponibilidad ni precios: solo llama herramientas. n8n orquesta la conversación;
toda regla de negocio vive en la API.

## 2. Actores y modelo de negocio

| Actor | Descripción |
|---|---|
| **Jugador** | Escribe al WhatsApp del club. No tiene cuenta; se identifica por su teléfono. |
| **Owner** | Dueño del club. Configura canchas, grilla, políticas y usuarios del panel. |
| **Staff** | Encargado/recepción. Gestiona la agenda y marca pagos / ausencias. |
| **SuperAdmin** | Operador de EZmatch. Alta de clubes y soporte. |

- **Multi-club desde el día 1** (multi-tenant): cada club tiene sus canchas, grilla,
  clientes y su inbox de Chatwoot. Todo dato de negocio lleva `ClubId`.
- **Monetización:** abono mensual por club (a definir, posiblemente por cantidad de canchas).
- **Pagos:** el jugador paga en caja al terminar el turno. **No hay seña** en el MVP;
  el staff marca la reserva como pagada desde el panel. (Seña con Mercado Pago queda como
  extensión futura si un cliente la pide.)

## 3. Reglas de negocio

### 3.1 Grilla fija de turnos
- Cada cancha tiene una **grilla semanal** de turnos (`SlotTemplate`): día de la semana,
  hora de inicio, duración y precio. Ej. pádel lun–vie 08:00, 09:30, 11:00 … de 90 min.
- El panel ofrece un **generador**: "de 08:00 a 23:30, cada 90 min, lun a vie, $X" →
  crea las filas; después se pueden editar individualmente (ej. precio de horario pico).
- Un turno **pertenece al día en que empieza**: el de 23:30 a 01:00 del viernes es del viernes.
- Horas en la **zona horaria del club** (por defecto `America/Argentina/Buenos_Aires`);
  en base de datos todo instante se guarda en UTC (`timestamptz`).
- Cambiar la grilla **no afecta reservas existentes**.

### 3.2 Disponibilidad
Un turno está libre para una fecha si:
1. existe en la grilla de esa cancha para ese día de la semana,
2. la cancha está activa,
3. no hay una reserva no cancelada que se superponga,
4. no hay un bloqueo que se superponga,
5. empieza después de `ahora + MinLeadMinutes` y dentro de `BookingHorizonDays`.

Para el bot, la disponibilidad se agrupa **por horario y deporte**, no por cancha:
"21:00 — 2 canchas libres — $30.000". Al reservar, la API asigna una cancha libre
(salvo que el jugador pida una puntual).

### 3.3 Reservas
- **Sin superposición garantizada por la base:** constraint de exclusión en Postgres
  (§5.3). Si dos reservas compiten, una recibe `409` y el bot ofrece alternativas.
- Estados: `Confirmed` → `Completed` | `Cancelled` | `NoShow`.
- Pago: `Unpaid` | `Paid` (lo marca el staff en caja).
- Origen: `WhatsApp` | `Panel`.
- Límite de reservas futuras activas por teléfono vía bot (`MaxActiveBookingsPerCustomer`,
  default 2) para evitar abusos. El panel no tiene límite.

### 3.4 Cancelaciones
- Por bot: solo reservas del **mismo teléfono** de la conversación y con al menos
  `CancellationMinHours` de anticipación (default 3 h). Si no se cumple, el bot deriva a
  una persona.
- Por panel: siempre permitido.

### 3.5 Clientes y ausencias
- `Customer` se crea automáticamente en la primera reserva (teléfono E.164 + nombre).
- El staff puede marcar `NoShow`. Un cliente puede quedar **bloqueado** (`IsBlocked`):
  el bot no le permite reservar y lo deriva a una persona. Importante al no haber seña.

## 4. Módulos funcionales

### 4.1 Bot de WhatsApp (jugador)
- Consultar disponibilidad ("¿hay pádel mañana a la noche?").
- Reservar (pide nombre la primera vez; confirma resumen antes de crear).
- Ver mis turnos / cancelar.
- Info del club: dirección, precios, políticas (desde `context`).
- Derivar a humano: pedido explícito, enojo, temas fuera de alcance, cliente bloqueado.
- (Fase 4) Recordatorio unas horas antes del turno.

### 4.2 Panel web (Owner / Staff)
- **Login** (email + contraseña, JWT).
- **Agenda del día**: columnas = canchas, filas = turnos de la grilla. Colores por estado.
  Click en turno libre → reserva manual; click en reservado → detalle, cancelar,
  marcar pagado / ausente. Navegación por día y vista semanal.
- **Reservas**: listado filtrable (fecha, estado, origen, cliente).
- **Clientes**: listado, historial, ausencias, bloquear/desbloquear.
- **Configuración** (solo Owner): datos del club, canchas, grilla (con generador),
  bloqueos, políticas (§3), usuarios del panel.
- **Superadmin**: alta/edición de clubes y vinculación con inbox de Chatwoot.

## 5. Modelo de datos

Convenciones: Ids `Guid` (no adivinables desde el bot), `CreatedAt`/`UpdatedAt` en UTC,
nombres de tablas/columnas en snake_case (Npgsql naming convention).

### Club — `clubs`
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | |
| Name, Slug | string | Slug único |
| Address, Phone | string? | Para el bot |
| TimeZone | string | IANA, default `America/Argentina/Buenos_Aires` |
| ChatwootAccountId, ChatwootInboxId | int? | Único (account, inbox) — así n8n resuelve el club |
| CancellationMinHours | int | default 3 |
| MinLeadMinutes | int | default 30 |
| BookingHorizonDays | int | default 14 |
| MaxActiveBookingsPerCustomer | int | default 2 |
| BotInstructions | string? | Texto libre extra para el prompt (ej. "se alquilan paletas") |
| IsActive | bool | |

### Court — `courts`
| Campo | Tipo | Notas |
|---|---|---|
| Id, ClubId | Guid | |
| Name | string | "Cancha 1", "Central" |
| Sport | enum | `Padel`, `Futbol5`, `Futbol7`, `Futbol11`, `Tenis` |
| IsCovered | bool | Techada (los jugadores preguntan por la lluvia) |
| SortOrder | int | Orden de columnas en la agenda |
| IsActive | bool | |

### SlotTemplate — `slot_templates`
| Campo | Tipo | Notas |
|---|---|---|
| Id, CourtId | Guid | |
| DayOfWeek | int | 0 = domingo |
| StartTime | TimeOnly | Hora local del club |
| DurationMinutes | int | |
| Price | decimal | ARS |
| | | Único (CourtId, DayOfWeek, StartTime) |

### Customer — `customers`
| Campo | Tipo | Notas |
|---|---|---|
| Id, ClubId | Guid | |
| Phone | string | E.164; único (ClubId, Phone) |
| Name | string | |
| IsBlocked | bool | |
| Notes | string? | |

### Booking — `bookings`
| Campo | Tipo | Notas |
|---|---|---|
| Id, ClubId, CourtId, CustomerId | Guid | ClubId desnormalizado para filtrar |
| StartsAt, EndsAt | timestamptz | UTC |
| Price | decimal | Copiado de la grilla al reservar |
| Status | enum | `Confirmed`, `Completed`, `Cancelled`, `NoShow` |
| PaymentStatus | enum | `Unpaid`, `Paid` |
| Source | enum | `WhatsApp`, `Panel` |
| CancelledAt, CancelReason | | |
| ReminderSentAt | timestamptz? | Fase 4 |
| CreatedByUserId | Guid? | Si vino del panel |

### Block — `blocks`
| Campo | Tipo | Notas |
|---|---|---|
| Id, CourtId | Guid | |
| StartsAt, EndsAt | timestamptz | |
| Reason | string | "Torneo", "Mantenimiento" |

### User — `users`
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | |
| ClubId | Guid? | Null para SuperAdmin |
| Email, PasswordHash, FullName | string | PBKDF2 como CalisApp |
| Role | enum | `SuperAdmin`, `Owner`, `Staff` |

Más `refresh_tokens` igual que CalisApp.

### 5.3 Constraint anti-superposición
EF no modela exclusiones; va en la migración con SQL crudo:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap
  EXCLUDE USING gist (court_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
  WHERE (status <> 'Cancelled');
```

El servicio captura la violación (`PostgresException.SqlState == "23P01"`) y la traduce a
`AppException.Conflict` con alternativas. Los bloqueos se validan en el servicio (y
opcionalmente con una exclusión análoga en `blocks`).

### Relaciones
```
Club 1─N Court 1─N SlotTemplate
Club 1─N Customer 1─N Booking N─1 Court
Court 1─N Block
Club 1─N User
```

## 6. Contrato de API

Convenciones como CalisApp: rutas en inglés, errores como `ProblemDetails` con `code`,
DTOs (nunca entidades), FluentValidation.

### 6.1 Bot — `/api/bot` (auth: header `X-Bot-Key`)
Diseñados para que los consuma un LLM: parámetros simples, respuestas cortas con un campo
`summary` en español listo para leer, y errores accionables.

El club se identifica por `inboxId` (de Chatwoot) y el jugador por `phone`;
ambos los inyecta n8n desde el webhook, **nunca los genera la IA**.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/bot/context?inboxId=` | Club: nombre, dirección, deportes y canchas, rango de precios, políticas, `BotInstructions`, fecha/hora actual local. Va al system prompt. |
| GET | `/api/bot/availability?inboxId=&date=&sport=&from=&to=` | Turnos libres agrupados por horario. `from`/`to` opcionales (HH:mm) para "a la noche". |
| POST | `/api/bot/bookings` | `{ inboxId, phone, customerName?, sport, date, startTime, courtId? }` → 201 con resumen. 409 con `alternatives[]`. 422 si falta el nombre de un cliente nuevo. 403 si está bloqueado o superó el límite. |
| GET | `/api/bot/bookings?inboxId=&phone=` | Próximas reservas de ese teléfono. |
| POST | `/api/bot/bookings/{id}/cancel` | `{ inboxId, phone }` — valida dueño y anticipación. |
| GET | `/api/bot/reminders/due?withinMinutes=` | Fase 4: reservas a recordar. |
| POST | `/api/bot/reminders/{bookingId}/sent` | Fase 4: marca recordatorio enviado. |

### 6.2 Panel — JWT
| Recurso | Rutas principales |
|---|---|
| Auth | `POST /api/auth/login`, `/refresh`, `/logout` |
| Agenda | `GET /api/agenda?date=` (canchas × turnos con estado), `GET /api/agenda/week?from=` |
| Reservas | `GET/POST /api/bookings`, `PUT /api/bookings/{id}`, `POST /{id}/cancel`, `/{id}/paid`, `/{id}/no-show` |
| Clientes | `GET /api/customers`, `GET /{id}`, `PUT /{id}`, `POST /{id}/block`, `/unblock` |
| Canchas | CRUD `/api/courts` |
| Grilla | `GET/PUT /api/courts/{id}/slots`, `POST /api/courts/{id}/slots/generate` |
| Bloqueos | CRUD `/api/blocks` |
| Club | `GET/PUT /api/club` (del usuario logueado) |
| Usuarios | CRUD `/api/users` (Owner) |
| Superadmin | CRUD `/api/admin/clubs` |
| Health | `GET /api/health` |

Toda consulta del panel se filtra por el `ClubId` del token (global query filter en EF).

## 7. Bot de WhatsApp (n8n + Chatwoot)

### 7.1 Workflow principal `EZmatch - Bot`
1. **Webhook** ← Chatwoot `message_created`.
2. **Filtro**: solo `incoming`, no privados, y conversación **sin asignar a un humano** y sin
   label `humano` (si un humano tomó la conversación, el bot no responde).
3. **Agrupar mensajes** (debounce de unos segundos) para no responder 3 veces a 3 mensajes seguidos.
4. **Contexto**: `GET /api/bot/context?inboxId=`.
5. **AI Agent** (Claude) con:
   - System prompt = instrucciones fijas + contexto del club + fecha/hora local.
   - Memoria: Postgres Chat Memory con `sessionId = conversation_id` de Chatwoot.
   - Tools (HTTP Request Tool; `inboxId` y `phone` fijos por expresión, la IA solo completa
     fecha/hora/deporte/nombre):
     `consultar_disponibilidad`, `reservar_turno`, `mis_turnos`, `cancelar_turno`,
     `derivar_humano` (sub-workflow: asigna en Chatwoot + label `humano` + nota interna).
6. **Responder** por la API de Chatwoot.
7. **Error branch**: si algo falla, mensaje amable + derivar a humano.

### 7.2 Reglas del prompt
- Nunca afirmar disponibilidad sin haber llamado `consultar_disponibilidad`.
- Antes de reservar, confirmar: deporte, día, hora y nombre. Reservar solo con "sí" explícito.
- Fechas relativas ("mañana", "el viernes") se resuelven con la fecha local del contexto.
- Mensajes cortos, tono rioplatense, sin markdown pesado (WhatsApp).

### 7.3 Recordatorios (Fase 4)
Workflow programado cada 15 min → `reminders/due` → envía **plantilla aprobada por Meta**
vía Chatwoot (fuera de la ventana de 24 h solo se pueden mandar plantillas) → marca enviado.

## 8. Stack y arquitectura

Mismo stack y convenciones que CalisApp.

### Backend — `backend/EZmatchApi`
- ASP.NET Core 10, controllers, EF Core 10 + **Npgsql**
  (`EFCore.NamingConventions` para snake_case).
- FluentValidation, Serilog, JWT + refresh tokens, `AppException` + `GlobalExceptionHandler`.
- Capas: `Controllers/` finos → `Services/` (reglas) → `Data/` (DbContext + configurations) ·
  `Models/` · `Dtos/`.
- Servicios clave: `AvailabilityService` (cálculo de turnos libres), `BookingService`
  (crear/cancelar, traducción de 23P01), `ClubContextService`, `SlotTemplateService`.
- Multi-tenant: `ICurrentClub` desde el JWT + global query filters; los endpoints de bot
  resuelven el club por `inboxId`.
- Tests: xUnit + **Testcontainers (Postgres real)** — la exclusión no se puede probar con
  SQLite/InMemory. Casos obligatorios: reservas concurrentes, turnos que cruzan medianoche,
  zona horaria, cancelación fuera de plazo.

### Frontend — `frontend/`
- React 19 + TypeScript + Vite + Tailwind v4 + React Query + React Router + Zustand +
  react-hook-form/zod (igual que CalisApp).
- Estructura `src/features/<feature>/{pages,components,api}` + `src/shared/`.
- Pensado para usarse en la PC de recepción **y en el celular del dueño** (responsive).

### Convenciones
- Código en inglés; UI, mensajes del bot y documentación en español.
- Ningún secreto en el repo.

## 9. Infraestructura y deploy

- **VPS Hostinger + Easypanel**, como CalisApp.
- `Dockerfile` (multi-stage sdk:10.0 → aspnet:10.0, puerto 5000).
- `docker-compose.prod.yml`: `api` + `postgres:17` (volumen persistente, healthcheck).
- `docker-compose.yml` local: solo Postgres en `localhost:5432`.
- Migraciones automáticas al arrancar en Production (igual que CalisApp).
- Panel: Vercel (o container nginx en Easypanel).
- n8n y Chatwoot ya existentes en el mismo VPS → la API se expone con dominio propio
  (ej. `api.ezmatch.app`) y n8n la llama por HTTPS.
- Backups diarios de Postgres (`pg_dump` programado).

## 10. Seguridad

- `X-Bot-Key`: clave larga aleatoria, guardada como credencial en n8n y comparada en tiempo
  constante. Rate limit en `/api/bot`.
- El bot solo opera sobre el `phone` de la conversación: un jugador no puede ver ni cancelar
  turnos ajenos aunque intente engañar a la IA (prompt injection) — lo garantiza la API, no el prompt.
- Ids `Guid`; `ClubId` validado en cada operación.
- Panel: JWT corto + refresh rotativo, PBKDF2, CORS restringido, autorización por rol en servidor.
- Datos personales mínimos (nombre + teléfono). Sin datos de pago.
- `.mcp.json`, `.env` y `appsettings.*.json` reales fuera del repo.

## 11. Roadmap

- **Fase 0 — Scaffolding:** monorepo, solución, API con `/api/health`, Postgres local
  (docker compose), Dockerfile + compose prod, frontend Vite vacío, AGENTS.md.
- **Fase 1 — Núcleo de reservas:** entidades + migraciones + exclusión, `AvailabilityService`,
  `BookingService`, seed de club demo ("Pádel Demo", 3 canchas de pádel + 1 de fútbol 5),
  tests con Testcontainers.
- **Fase 2 — Bot:** endpoints `/api/bot`, workflow n8n completo con Chatwoot, prueba
  punta a punta con un inbox de prueba.
- **Fase 3 — Panel:** auth, agenda día/semana, reservas manuales, clientes,
  configuración de canchas/grilla/bloqueos/políticas.
- **Fase 4 — Producción y piloto:** recordatorios con plantilla Meta, derivación a humano
  pulida, deploy productivo, onboarding del primer club.
- **Fase 5 — Extensiones:** turnos fijos semanales, lista de espera ("avisame si se libera"),
  "falta uno", seña con Mercado Pago (si un cliente la pide), métricas de ocupación, torneos.

## 12. Decisiones abiertas

- Precio del abono y si se cobra por cancha.
- ¿Una cuenta de Chatwoot por club o un account compartido con un inbox por club?
  (Afecta permisos del staff si responde desde Chatwoot.)
- Turnos fijos semanales: muy comunes en fútbol 5 — evaluar subirlos a Fase 3.
- Modelo de IA para el agente (calidad vs. costo por conversación).
