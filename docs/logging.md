# Logging and troubleshooting

All three applications log the same way: one configuration section (`ActivityLogging`), one document format
(Elastic Common Schema JSON) and two destinations — local files and/or Elasticsearch (Kibana). Switching between
them is a configuration change; the code and the documents stay the same.

The shared code is the `Starter.Observability` project (a stable module: configure it, don't change it).

## What is logged

| Category | Switch | What | Written by |
| --- | --- | --- | --- |
| Errors | `Categories:Errors` | Warnings, errors and exceptions (with stack traces) from any part of any app | all |
| Security | `Categories:Security` | Sign-in (success/failure with reason), sign-out, registration, email confirmation, password reset/change, profile change, Google, token issue and refresh, session end, account emails sent/failed | Identity, Web |
| Api | `Categories:Api` | Every API call — once by the caller (`api.call` in Web) and once by the API (`api.request`) — with route, path, status, duration and, for errors, the problem title and validation messages | Web, Api |
| Frontend | `Categories:Frontend` | Page views, tab connect/disconnect/reconnect, UI actions you log yourself | Web |

`Enabled: false` switches off everything that is stored. Console output (what you see in Visual Studio or
`docker compose logs`) is not affected.

### Ready-made combinations

| Level | Settings |
| --- | --- |
| 0 – nothing | `Enabled: false` |
| 1 – errors only | `Errors: true`, others `false` |
| 2 – errors + security + API (**default**) | `Errors`, `Security`, `Api`: `true`; `Frontend: false` |
| 3 – everything | all `true` |

Categories and `Enabled` are re-read while the app runs (edit `appsettings*.json` or the App Service setting; applies
within seconds). `Destination`, the file path, retention and the Elasticsearch connection apply after a restart.

## Configuration

```json
"ActivityLogging": {
  "Enabled": true,
  "Destination": "File",              // File | Elasticsearch | Both
  "RetentionDays": 7,                 // local files
  "Categories": { "Errors": true, "Security": true, "Api": true, "Frontend": false },
  "File": { "Path": "logs" },
  "Elasticsearch": { "Url": "", "ApiKey": "", "Namespace": "production" }
}
```

| Where you run | How to switch |
| --- | --- |
| Visual Studio / `dotnet run` | `src/Starter.<App>/appsettings.Development.json` (already points at `http://localhost:9200`); set `"Destination": "Both"` |
| Docker Compose | `.env`: `LOG_DESTINATION=File`, `Elasticsearch` or `Both`; `LOG_RETENTION_DAYS=7` |
| Azure App Service | App settings `ActivityLogging__Destination`, `ActivityLogging__Categories__Frontend`, … |

`ApiKey` is a secret: user secrets locally, Key Vault in Azure.

## Local files

```
logs/
  identity/identity-20261121.json     sign-in, registration, emails, tokens  (security problems)
  api/api-20261121.json               API requests and server errors        (data problems)
  web/web-20261121.json               API calls as the UI saw them, UI      (what the user experienced)
```

- `dotnet run` / Visual Studio: `logs/` in the repository root (git-ignored).
- Docker Compose: the `app-logs` volume, e.g. `docker compose exec web ls /app/logs/web`.
- One JSON document per line; files older than `RetentionDays` are deleted automatically.
- Every document has `user.id`, so you never need a folder per user. Quick filter without Kibana:

```bash
cat logs/*/*.json | grep '"id":"<user id>"'
```

## Kibana (local)

```bash
docker compose --profile logs up -d --wait
```

Starts Elasticsearch (127.0.0.1:9200) and Kibana (http://localhost:5601) and creates:

- the index template `logs-starter` for the data streams `logs-starter.<app>-<namespace>` (ECS mappings, deletion
  after `LOG_RETENTION_DAYS` days, default 7),
- the data view **Starter logs** covering all three applications.

Then set the destination to `Both` (or `Elasticsearch`) and restart the apps. Development only: security is switched
off and the ports are bound to 127.0.0.1. It needs about 2–3 GB of memory.

### Finding things in Discover (data view "Starter logs")

| Question | KQL query |
| --- | --- |
| Everything one user did | `user.id : "<id>"` |
| One user's problems | `user.id : "<id>" and event.outcome : "failure"` |
| The whole story of an error the user reported (reference number) | `trace.id : "<reference>"` |
| All failed API calls | `event.action : ("api.call" or "api.request") and event.outcome : "failure"` |
| Server errors only | `log.level : "Error"` |
| Failed sign-ins | `event.action : "auth.sign_in" and event.outcome : "failure"` |
| Emails that were not sent | `event.action : "email.send" and event.outcome : "failure"` |
| Only one application | `service.name : "api"` (or `identity`, `web`) |

Useful columns: `service.name`, `event.action`, `event.outcome`, `user.id`, `trace.id`,
`http.response.status_code`, `labels.Reason`, `labels.Path`, `message`.

## Reference numbers

When an API call fails, the web app shows *"Reference: 4bf92f3577b34da6a3ce929d0e0e4736"*. It is the `trace.id` of
that call. Web starts a new trace for every API call and passes it to the API (W3C `traceparent` header), so the
web entry (`api.call`), the API entry (`api.request`) and any exception logged while handling it all share it.
Search `trace.id : "<reference>"` to see the complete story.

## Event catalogue

| `event.action` | App | Notes (`labels.Reason` and other fields) |
| --- | --- | --- |
| `auth.sign_in` | identity | `Method` password/Google; failure reasons `unknown_email`, `wrong_password`, `locked_out`, `email_not_confirmed`, `no_linked_account`; `Email` masked |
| `auth.external_sign_in` | identity | Google cancelled/failed |
| `auth.token` | identity | `Grant` authorization_code/refresh_token; failures `account_deleted`, `not_allowed`, `security_stamp_changed` |
| `auth.sign_out` | identity, web | |
| `auth.sessions_revoked` | identity | after password change/reset |
| `auth.web_sign_in` | web | sign-in completed in the web app, or failed with the reason |
| `auth.token_refresh` | web | access token refreshed |
| `auth.session_ended` | web | refresh token rejected → user must sign in again |
| `account.register` | identity | failures `email_already_registered`, `email_registered_not_confirmed`, Identity error codes |
| `account.confirm_email` | identity | `invalid_link`, `invalid_or_expired_token`, `already_confirmed` |
| `account.resend_confirmation`, `account.password_reset_request` | identity | outcome `unknown` when nothing was sent (unknown or already confirmed email) |
| `account.password_reset`, `account.password_change`, `account.password_set` | identity | |
| `account.profile_update`, `account.external_login_link`, `account.external_login_remove` | identity | |
| `email.send` | identity | `Subject`, masked `Recipient`, `SmtpHost`; failures `per_recipient_limit_reached` or the SMTP exception |
| `api.call` | web | `ApiStatus`, `StatusCode`, `DurationMs`, `Reason` (`timeout`, `connection_failed`, `session_ended`, …), `ProblemTitle`, `ValidationErrors` |
| `api.request` | api | `Route`, `Path`, `StatusCode`, `DurationMs`, `ProblemTitle`, `ValidationErrors` |
| `ui.page_view`, `ui.circuit_opened`, `ui.connection_lost`, `ui.connection_restored`, `ui.circuit_closed` | web | Frontend category |

## Logging your own events

```csharp
// Server code (API endpoint, identity page): any ILogger
logger.LogActivity(LogCategories.Api, "invoice.created", EventOutcome.Success,
    new Dictionary<string, object?> { ["InvoiceId"] = invoice.Id });

// Blazor component: FrontendActivity (Frontend category)
@inject FrontendActivity Frontend
await Frontend.LogAsync("ui.export_clicked");
```

Use stable, dotted action names. API calls are already logged by `ApiClient` and the API middleware — don't log them again.

## What is never logged

Passwords, tokens, cookies, session keys (only a non-reversible hash), codes from email links, request bodies and
user-entered content (validation logs contain field names and messages, not values). Email addresses appear only
masked (`l***@gmail.com`); users are identified by `user.id`. Logs with `user.id` are still personal data: keep the
retention short and restrict who can open Kibana. Integration tests check that passwords and tokens do not appear in
the files.

## Production

- **Elasticsearch/Kibana:** self-hosted (free basic licence; you run the server) or Elastic Cloud (paid, trial
  available; also via Azure Marketplace). Set `Destination: Elasticsearch` (or `Both`), `Url`, `ApiKey` (an API key
  with permission to write to `logs-starter.*`) and `Namespace` (e.g. `production`). Create the index template once
  with the request in `deploy/elastic/setup.sh` against your cluster (adjust retention there).
- **Azure App Service:** local files are not a durable store — use Elasticsearch. Container stdout is still available
  through App Service log streaming.
- If Elasticsearch is unreachable the apps keep working: documents are buffered in memory and dropped when the
  buffer is full. With `Both`, the files still have everything.
