# Deploy — Hostinger VPS + Easypanel

```
WhatsApp → Chatwoot ─webhook→ n8n ─HTTPS + X-Bot-Key→ EZmatch API (.NET) → PostgreSQL
Panel (navegador) ──HTTPS──► web (nginx) ──/api──────────────┘
                     (todo en el mismo VPS, Easypanel)
```

`docker-compose.prod.yml` levanta tres servicios: `api`, `postgres` y `web` (panel: nginx que
sirve el frontend y proxea `/api` a la API, así panel y API comparten dominio y no hay CORS).
Las migraciones se aplican solas al arrancar.

## 1. Código en GitHub

Easypanel construye la imagen desde el repo privado
[augrolandelli/EZmatch](https://github.com/augrolandelli/EZmatch), rama `main`.
Cada `git push` a `main` + Deploy en Easypanel publica la versión nueva.

## 2. Crear el servicio en Easypanel

1. Proyecto nuevo `ezmatch` (o dentro del proyecto existente).
2. **+ Service → Compose**.
3. Source: **GitHub** → repo `EZmatch`, rama `main`, archivo `docker-compose.prod.yml`.
4. **Environment** (copiar de `.env.example` y completar):

   ```bash
   POSTGRES_PASSWORD=<openssl rand -base64 24>
   BOT_API_KEY=<openssl rand -hex 32>
   FRONTEND_URL=https://panel.ezmatch.app   # todavía no hay panel: cualquier URL sirve
   SEED_DEMO_CLUB=true                      # crea "Pádel Demo" para probar el bot
   SEED_CHATWOOT_INBOX_ID=<id del inbox de prueba en Chatwoot>
   JWT_KEY=<openssl rand -base64 48>         # firma de las sesiones del panel
   ADMIN_EMAIL=<tu email de login>          # SuperAdmin, se crea solo la primera vez
   ADMIN_PASSWORD=<contraseña>
   SEED_DEMO_OWNER_EMAIL=<opcional>         # dueño del club demo para probar el panel
   SEED_DEMO_OWNER_PASSWORD=<opcional>
   ```

5. **Domains** (dos dominios, ambos con HTTPS):
   - API (la usa n8n): servicio compose **`api`**, puerto **5000**. Ej. `ezmatch-backend.<sub>.easypanel.host`.
   - Panel (lo usa el club): servicio compose **`web`**, puerto **80**. Ej. `ezmatch.<sub>.easypanel.host`.
6. Deploy.

## 3. Verificar

```bash
curl https://<dominio-api>/api/health
# {"status":"healthy","service":"EZmatchApi","database":"up",...}

curl -H "X-Bot-Key: <BOT_API_KEY>" "https://<dominio-api>/api/bot/context?inboxId=<SEED_CHATWOOT_INBOX_ID>"
```

## 4. Conectar n8n

- Credencial **Header Auth** en n8n: nombre `X-Bot-Key`, valor = `BOT_API_KEY`.
- En el nodo `config` del workflow del bot: `api_url` = dominio de la API.

## Notas

- **El id del inbox** se ve en Chatwoot → Settings → Inboxes → (inbox) → la URL termina en `/inboxes/<id>`.
- Cambiar `SEED_CHATWOOT_INBOX_ID` y redeployar vuelve a vincular el club demo a otro inbox.
- **Apagar el seed cuando haya clubes reales:** `SEED_DEMO_CLUB=false` y vaciar `SEED_CHATWOOT_INBOX_ID`,
  `SEED_DEMO_OWNER_EMAIL` y `SEED_DEMO_OWNER_PASSWORD`. Mientras está prendido, en cada arranque vuelve a
  vincular el inbox al club demo (si ese inbox ya es de otro club, solo avisa en el log).
- **Clubes nuevos:** se dan de alta desde el panel (menú Clubes, solo SuperAdmin), con su dueño y su inbox
  de Chatwoot. El bot responde en la cuenta de Chatwoot de donde vino cada mensaje (una cuenta por club).
- **Backups**: el volumen `postgres-data` guarda la base. Programar un `pg_dump` diario
  (Easypanel → servicio postgres → Backups, o un cron en el VPS).
