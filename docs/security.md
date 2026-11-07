# Security

This page lists the protections built into the starter, where they live, and what the design does not cover.

## Controls

### Accounts (Starter.Identity)

| Control | Implementation |
| --- | --- |
| Password storage | ASP.NET Core Identity (PBKDF2 with per-user salt) |
| Password policy | At least 12 characters, at least 4 different characters, no composition rules (NIST SP 800-63B) |
| Brute force | Lockout after 5 failed attempts for 15 minutes; per-IP limit on account form posts (20/min by default) |
| Email confirmation | Required before sign-in; links only sent by email, never shown on a page; links expire after 4 hours |
| Account enumeration | Same response for unknown/known emails on sign-in, registration (the owner gets an email instead), reset and resend |
| Email flooding | At most 5 account emails per recipient per hour, plus the per-IP form limit |
| Password reset poisoning | Email links built from the configured public URL (`Authentication:Issuer`), never from the Host header |
| Google | Sign-in only to an explicitly linked account. A matching email never links accounts automatically; linking requires being signed in and is verified with the XSRF value bound to the user id. New accounts skip email confirmation only if Google reports the email as verified |
| Google not configured | The scheme is not registered and the button is not rendered |
| Credential change | Security stamp updated; all authorizations and refresh tokens of the user revoked |
| Open redirects | Return URLs must be local or an origin of a registered client redirect URI |
| CSRF | Antiforgery tokens on every form (Razor Pages default); end-session without a valid `id_token_hint` requires a confirmation form with antiforgery |

### Tokens (OpenIddict)

| Control | Implementation |
| --- | --- |
| Flow | Authorization Code + PKCE (S256) required for every client; no implicit, password or client-credentials flows |
| Client authentication | Web app is a confidential client (secret); redirect and post-logout URIs must match exactly |
| Audience | Access tokens carry `aud=starter-api` only when the `starter_api` scope was granted |
| Access tokens | Signed JWT (RS256, `typ: at+jwt`), 5 minutes. Not encrypted, so the API needs only public keys |
| Refresh tokens | Encrypted, stored as token entries, rotated on use, revocable; refused after a security stamp change |
| Keys | Signing and encryption certificates from configuration (Key Vault / App Service certificates in Azure); development certificates only in `Development` |

### Web app (Starter.Web)

| Control | Implementation |
| --- | --- |
| Tokens never in the browser | Server-side session store (`web.Sessions`), encrypted with Data Protection; the cookie holds only a random key |
| Cookie | `__Host-starter-web`: HttpOnly, Secure, SameSite=Lax, path `/`, no domain |
| Per-user isolation | Scoped `ApiClient` per circuit; tokens read per session key; no static or shared token state; the Authorization header is set per request message |
| Circuits | Session revalidated every minute; ended sessions redirect to sign-in |
| CSRF | Blazor forms run over the circuit; the sign-out POST validates an antiforgery token explicitly |
| Errors | Friendly messages only; no stack traces, tokens or HTTP details in the UI |

### API (Starter.Api)

| Control | Implementation |
| --- | --- |
| Authentication | OpenIddict validation: signature (keys from discovery), issuer, audience, lifetime, token type |
| Authorization | Fallback policy: every endpoint needs a valid token with the `starter_api` scope unless marked anonymous (health checks only) |
| Ownership | User id from the `sub` claim; every query filters by owner; foreign records return 404; owner fields in requests are rejected (unknown JSON members → 400) |
| Input | DataAnnotations validation (shared with the UI), length limits, 64 KB request body limit, page size ≤ 50 |
| Rate limiting | 300 requests/min and 60 writes/min per user (configurable), 429 with `Retry-After` |

### Everywhere

- HTTPS only (HSTS outside Development, `UseHttpsRedirection`); certificate validation is never disabled in code.
- Security headers: CSP (`script-src 'self'`, no inline script), `frame-ancestors 'none'`, `X-Content-Type-Options`,
  `Referrer-Policy`.
- No secrets in the repository: `.env`, `.certs/`, user secrets locally; Key Vault in Azure.
- Logs never include passwords, tokens or codes (OpenIddict logging at Warning; PII logging off; email addresses
  are not logged by the email sender).
- Containers run as the non-root `app` user; least-privilege database users per application.

## Limits of this design

- **Access tokens after sign-out:** a JWT access token stays valid until it expires (5 minutes). It exists only on
  the web server and the session holding it is deleted at sign-out. For immediate revocation switch the API to
  introspection or token-entry validation (costs a lookup per request).
- **Other devices after a password change:** they lose access within about 6 minutes (token lifetime + revalidation).
- **In-memory counters:** rate limits and the per-recipient email limit are per instance. With several instances
  use a shared store (Redis, SQL) if exact limits matter.
- **Refresh lock** is per instance; concurrent refreshes from two instances rely on OpenIddict's refresh token
  reuse leeway.
- **Data Protection keys** are stored in SQL without additional encryption at rest beyond the database's own
  (TDE in Azure SQL). Add `ProtectKeysWithAzureKeyVault` for defence in depth.
- **Not included:** two-factor authentication, account deletion/export, admin panel, roles, CAPTCHA, audit log.
- **Google**: the browser part of the Google flow cannot run in normal CI; it is verified manually
  ([google-sign-in.md](google-sign-in.md)). The callback handling, cancellation and the "no automatic linking"
  rule are covered by integration tests where possible.

## Reporting checklist for new features

- Is the endpoint under `/api` (protected by default)? If it is anonymous, why?
- Does every query on user data filter by `user.GetUserId()`?
- Are request models free of fields the server should decide (owner, ids, timestamps)?
- Are lengths and sizes limited, lists paged?
- Is there an integration test for "another user cannot read/change it"?
