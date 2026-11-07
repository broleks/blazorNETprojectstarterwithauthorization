# Google sign-in

Google is an *external login* linked to a local account. Google tokens are used only by the identity server to
learn who the user is; the API never accepts them (it accepts only access tokens issued by Starter.Identity).

## Configure

1. In Google Cloud Console → *APIs & Services* → *Credentials*, create an **OAuth client ID** of type
   *Web application*.
2. Authorized redirect URIs:
   - local (both variants): `https://localhost:7001/signin-google`
   - each deployed environment: `https://<identity host>/signin-google`
3. Configure the *OAuth consent screen* (scopes `openid`, `email`, `profile` are enough).
4. Provide the credentials:
   - `dotnet run`: `dotnet user-secrets set "Authentication:Google:ClientId" "<id>" --project src/Starter.Identity`
     (and `ClientSecret`)
   - Docker Compose: `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` in `.env`, then `docker compose --profile app up -d`
   - Azure: Key Vault secrets `google-client-id`, `google-client-secret` and Bicep parameter `enableGoogleSignIn=true`

Without both values Google is not registered and the "Continue with Google" button does not appear.

## Rules implemented

- A Google account signs in only to the local account it is linked to.
- First Google sign-in with an unknown email: the user chooses a display name and a new account is created.
  The email is taken from Google, never from the form; it counts as confirmed only if Google says it is verified.
- First Google sign-in with an email that already has a local account: **no automatic linking**. The user is told to
  sign in with the password and connect Google under *Your account → Sign-in methods*.
- Linking from *Sign-in methods* requires being signed in; the callback checks that the external sign-in was started
  by the same user. A Google account already linked elsewhere is refused.
- Disconnecting is allowed only when another sign-in method (password or another login) remains.
- Cancel on Google's consent screen → back to sign-in with "Google sign-in was cancelled".

## Automated vs manual verification

| Automated (CI, `Starter.Api.Tests`, `Starter.E2E.Tests`) | Manual (real Google) |
| --- | --- |
| Button hidden without configuration; shown with configuration | Full sign-in with a Google test account |
| Challenge redirects to `accounts.google.com` with our client id, callback URL and state | Account creation with display name |
| Cancel (`error=access_denied`) returns to sign-in with a message | Existing email → no automatic link, message shown |
| Forged state fails safely | Link/unlink from *Sign-in methods*; sign-in with Google afterwards |

### Manual checklist

Run with real credentials (local or a test environment) and a Google test account:

1. Sign-in page shows *Continue with Google*. Click → Google → *Cancel* → message "Google sign-in was cancelled".
2. Click again, choose the account → *Create your account* page with your Google email → enter a display name →
   you land in the app; the dashboard shows the display name.
3. Sign out. Register a password account with a **different** email, then sign in with Google using an account whose
   email equals that password account's email → "You already have an account", no access granted.
4. Sign in with the password, open *Account → Sign-in methods → Connect Google*, complete Google → "Google is now
   connected". Sign out, *Continue with Google* → signed in to the same account.
5. *Disconnect* Google → Google sign-in now offers to create a new account (or reports the existing email).
