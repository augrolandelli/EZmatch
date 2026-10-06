# Deploy — Hostinger VPS + Easypanel

```
WhatsApp → Chatwoot ─webhook→ n8n ─HTTPS + X-Bot-Key→ EZmatch API (.NET) → PostgreSQL
                                     (mismo VPS, Easypanel)
```

La API y su Postgres se levantan juntos con `docker-compose.prod.yml`.
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
   ```

5. **Domains**: asignar un dominio al servicio `api`, puerto **5000**, con HTTPS.
   Ej.: `https://ezmatch-api.<tu-subdominio>.easypanel.host`.
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
- Cuando haya clubes reales (Fase 3, panel), poner `SEED_DEMO_CLUB=false`.
- **Backups**: el volumen `postgres-data` guarda la base. Programar un `pg_dump` diario
  (Easypanel → servicio postgres → Backups, o un cron en el VPS).
