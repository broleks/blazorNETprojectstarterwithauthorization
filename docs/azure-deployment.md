# Azure deployment (prepared, not yet performed)

This is a plan with the files needed to deploy. **Nothing has been deployed or tested in Azure.** The Bicep template
compiles (`bicep build`) but has not been run against a subscription. Review names, SKUs and costs first.

## Target

| Resource | Purpose |
| --- | --- |
| App Service plan (Linux, P0v3) + 3 Web Apps for Containers | `identity`, `api`, `web` |
| Azure Container Registry (Basic) | Images; App Services pull with their managed identity (AcrPull) |
| Azure SQL (logical server + database) | Microsoft Entra authentication only; apps connect with managed identities |
| Key Vault (RBAC) | Web client secret, SMTP password, Google credentials, token certificates |
| Log Analytics + Application Insights | Logs and failures |

Template: [`deploy/azure/main.bicep`](../deploy/azure/main.bicep), example parameters:
[`main.parameters.example.json`](../deploy/azure/main.parameters.example.json).

## 1. Infrastructure

```bash
az group create -n rg-starter-prod -l westeurope
```

```bash
az deployment group create -g rg-starter-prod -f deploy/azure/main.bicep -p @deploy/azure/main.parameters.prod.json
```

(Copy the example parameters file to `main.parameters.prod.json`; keep it out of git if it contains anything sensitive.)

## 2. Secrets and certificates (outside the repository)

In the Key Vault from the deployment outputs:

- `web-client-secret` — random 32+ bytes; used by both identity and web.
- `smtp-password` — if the SMTP relay needs credentials (`smtpUserName` parameter).
- `google-client-id`, `google-client-secret` — then redeploy with `enableGoogleSignIn=true`.
- Token certificates: create two certificates in Key Vault (e.g. `openiddict-signing`, `openiddict-encryption`,
  RSA 2048+, exportable), import them into the identity App Service (*Certificates → Bring your own → From Key Vault*),
  and redeploy with `signingCertificateThumbprint` / `encryptionCertificateThumbprint`. App Service exposes them as
  `/var/ssl/private/<thumbprint>.p12` (`WEBSITE_LOAD_CERTIFICATES`). Rotation: add the new certificate, deploy,
  keep the old one until issued refresh tokens have expired.

## 3. Database users

Connect to the database as the Entra admin group and run [`deploy/sql/azure-sql-users.sql`](../deploy/sql/azure-sql-users.sql)
with the names of the three App Service managed identities (deployment output `appIdentityNames`) and the identity
your pipeline uses for migrations. Apps get read/write on their own schema only; the migrator gets DDL rights.

## 4. Database migrations

Never at app startup. Run them as a deployment step with the migrator identity, before switching the apps to the
new image. From a pipeline signed in to Azure as the migrator identity (e.g. `azure/login` with OIDC):

```bash
dotnet run --project src/Starter.Identity -c Release -- --migrate
```

with `ConnectionStrings__Migrations="Server=tcp:<server>.database.windows.net,1433;Database=<db>;Encrypt=True;Authentication=Active Directory Default"`
and the identity server's production settings (issuer, clients, certificates) available, because this step also
registers the OpenIddict clients. Repeat for `src/Starter.Api` and `src/Starter.Web` (they only need the connection
string and their required settings). Alternative: `dotnet ef migrations bundle` per project (schema only — then run
the identity `--migrate` step for client registration).

## 5. Images and release

```bash
az acr login -n <registry>
```

```bash
docker build -f src/Starter.Identity/Dockerfile -t <registry>.azurecr.io/starter-identity:<tag> .
```

```bash
docker push <registry>.azurecr.io/starter-identity:<tag>
```

Repeat for `api` and `web` (or `az acr build`). Then migrate (step 4) and redeploy the Bicep template with
`imageTag=<tag>` (or `az webapp config container set`). Use deployment slots if you need zero-downtime swaps.

## HTTPS, proxy and addresses

- App Service terminates TLS; the containers listen on HTTP 8080. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the
  apps see `https` and the client IP (needed for redirects, cookies and rate limiting). HTTPS-only, TLS 1.2+.
- Custom domains: add them with managed certificates, then set `identityHostName`, `webHostName`, `apiHostName`.
  The issuer, OpenIddict redirect URIs, the Google redirect URI (`https://<identity>/signin-google`) and
  `Authentication:DefaultReturnUrl` follow from these values.
- Set `AllowedHosts` to your host names.

## Blazor Interactive Server in Azure

- WebSockets must be enabled on the web app (the template does this). Without them the circuit falls back to long
  polling.
- A circuit lives in the memory of one instance. With more than one instance, **ARR affinity (sticky sessions) must
  stay enabled** (the template enables it for the web app). An instance restart or scale-in drops the circuits on it;
  users see the reconnect dialog and the page reloads (the server-side session survives, so they stay signed in).
- Many concurrent users: consider Azure SignalR Service (not configured here).

## Scaling out: what is and is not ready

| Concern | Status |
| --- | --- |
| Data Protection keys (cookies, sessions, email tokens) | Shared in SQL per app — ready |
| Web sessions and tokens | Shared in SQL — ready |
| Token signing keys | Shared certificates — ready |
| Blazor circuits | Need ARR affinity (configured) |
| Rate limits, email throttling, refresh lock | In memory per instance — limits become per instance |
| Verified with more than one instance | **No** — test before relying on it |

The starter is therefore *designed* for several instances but not declared ready until you have tested it.

## Monitoring and health

- `/health/live` (process) and `/health/ready` (database; for web also identity server and API). App Service health
  check uses `/health/ready` and replaces unhealthy instances.
- Application Insights connection string is set; to collect telemetry add the Azure Monitor OpenTelemetry
  distro (`Azure.Monitor.OpenTelemetry.AspNetCore`, `builder.Services.AddOpenTelemetry().UseAzureMonitor()`) or rely on
  App Service log streaming / container logs sent to Log Analytics (diagnostic settings).
- Errors are logged with request ids; users see only the reference id.

## Optional hardening

- Private endpoint for Azure SQL and VNet integration for the apps (remove the "Allow Azure services" rule).
- Encrypt Data Protection keys with Key Vault (`Azure.Extensions.AspNetCore.DataProtection.Keys`,
  `ProtectKeysWithAzureKeyVault`).
- Front Door / WAF in front of the identity server.
