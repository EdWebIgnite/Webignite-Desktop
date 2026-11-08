# The Webignite sync server

The sync server is the one piece of Webignite that doesn't run on the PC: it holds accounts and workspaces, and
(from phase B step 3) the records the apps sync. It is `src/Webignite.Server`, an ASP.NET Core app with its own
SQLite database. A step-by-step guide to putting it on a server comes with phase B step 6.

## How accounts stay safe

- **The password never leaves the PC.** The app stretches it with PBKDF2-SHA256 (600 000 rounds, a random salt
  per account) and splits the result into two keys (`AccountKeys` in Core): a **sign-in key**, sent to the server
  to prove who you are, and an **encryption key**, which never leaves the PC and will lock the workspace key, so
  the server can't read anything synced.
- **The server keeps only hashes.** The sign-in key is hashed again (PBKDF2, its own salt) before it's stored.
  Access and refresh tokens are stored as SHA-256 hashes, never as they are.
- **Two-factor** with any authenticator app (RFC 6238 codes, `Totp` in Core). Each code works once. Turning it on
  gives eight one-time recovery codes, stored hashed. The secret is kept sealed with the server's own key.
- **Wrong tries:** ten wrong passwords lock the account for 15 minutes, and each address can try signing in at most
  20 times a minute.
- **Devices:** each signed-in PC has a 15-minute access token and a 30-day refresh token. A refresh token works
  once; using an old one again signs that PC out, as someone may have copied it.
- **No giveaways:** asking for the salt of an email with no account gives a made-up one that's the same every
  time, so the server doesn't reveal who has an account.
- **HTTPS only:** the app refuses a server address that isn't `https://` (except one on the same PC, for testing).
  On a real server, Caddy in front of it does HTTPS; the sync server only listens on the machine itself.

## Settings

| Environment variable | What it is |
| --- | --- |
| `WEBIGNITE_SERVER_SECRET` | 32 or more random bytes, Base64 (`openssl rand -base64 32`). Required outside Development. It seals two-factor secrets and makes the stand-in salts. **Never put it in the repository**; losing it means everyone sets up two-factor again. |
| `WEBIGNITE_SERVER_DATA` | The folder for `webignite-server.db` (default `./data`). Back this folder up. |

## Trying it on this PC

```
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Webignite.Server --urls http://localhost:5080
```

Then in Webignite, Settings, Account and sync, use `http://localhost:5080` as the sync server.

## Endpoints

| Method and path | What it does |
| --- | --- |
| `POST /api/kdf` | The salt and rounds to stretch an email's password with. |
| `POST /api/accounts` | A new account, with a new workspace or joining one with an invite code. |
| `POST /api/sessions` | Sign in (with a two-factor or recovery code when it's on). |
| `POST /api/sessions/refresh` | New tokens for a signed-in PC. |
| `DELETE /api/sessions/current` | Sign this PC out. |
| `GET /api/account` | The account, its workspace and the people in it. |
| `POST /api/two-factor`, `/api/two-factor/confirm`, `/api/two-factor/off` | Turn two-factor on (with recovery codes) or off. |
| `POST /api/workspace/invites` | An invite code for the workspace (owner only), one use, seven days. |
| `GET /health` | For the server's own monitoring. |
