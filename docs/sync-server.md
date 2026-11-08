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
- **The workspace key** (`WorkspaceKeys` in Core) is 32 random bytes made on the PC when a workspace is created.
  Everything synced will be locked with it. The server only ever holds it locked (AES-256-GCM): once per person
  with the encryption key from their password, once per person with their recovery key, and once per invite with
  the invite's secret half. Each lock names what it's for, so one can't be passed off as another, and a wrong key
  or a changed lock is noticed. The app keeps the unlocked key in Windows Credential Manager.
- **Recovery key:** 52 letters and digits shown once when the account is made, to write down or print. With it and
  the email, a forgotten password is replaced: the app unlocks the workspace key with it, locks it with the new
  password, and the server signs every other PC out. The server keeps only a hash of a check made from it.
- **Invite codes** have two halves: the server's half (it knows the invite) and a secret half that unlocks the
  workspace key and is never sent to the server. Give the whole code to your partner privately.
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

## How shared projects sync

- **Locked before they leave.** Each record (a project, section, task, comment, activity line, or who's who) is
  turned into JSON on the PC and locked with AES-256-GCM, using a key made from the workspace key
  (`RecordLock` in Core). The record's kind and id are bound into the lock, so the server can't swap one record
  for another or change one unnoticed: a record that doesn't open is left out and counted.
- **What the server sees:** the workspace, each record's kind and id, its size, and the order records arrived in.
  Never names, titles, notes or anything inside.
- **Only shared projects.** Projects ticked Shared, with their sections, tasks, comments and activity. Personal
  projects never leave the PC. Attached files don't sync yet.
- **Merging.** When a record comes in that this PC also has, each field keeps whichever change was made later, by
  the stamps every record carries (`RecordMerge`). You and your partner can change different things on the same
  task at the same time and both are kept. A deletion stays deleted unless the other PC changed the record after it.
  Who follows and who liked something are stamped person by person, so you both liking a task at once keeps both.
- **Who's who.** Each PC made its own ids for "me" and "my partner" before anything was shared. The first of the
  owner's PCs to sync sends its ids; every other PC takes them on and changes its own records over, so tasks stay
  given to the right person (`TeamIds`). The partner's PC sends nothing until it has them.
- **Only what changed goes.** The PC keeps a fingerprint of what it last sent per record (table `sync_sent`) and
  sends only records whose fingerprint differs. It pulls everything after its cursor first, then pushes. It runs
  every minute while signed in, and from Sync now in Settings (`SyncService` in Services).
- **Limits:** at most 500 records a push and a pull, ids of up to 64 letters, digits, `-` and `_`, and 512 KB a
  record. A record too big for the server stays on the PC and is counted.

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
| `POST /api/workspace/invites` | An invite for the workspace (owner only), one use, seven days, carrying the workspace key locked with the invite's secret half. |
| `POST /api/invites/open` | The workspace's name and locked key for an invite's server half, before signing up. |
| `POST /api/recovery/key` | The recovery-locked workspace key, for the right recovery check. |
| `POST /api/recovery/reset` | A new password with the recovery key; every device is signed out. |
| `POST /api/sync/push` | Locked records for the caller's workspace (`{records: [{kind, id, payload}]}`); each gets the next number. Returns the last number. |
| `POST /api/sync/pull` | Locked records numbered after `since`, oldest first, up to 500, with the new cursor and whether there are more. |
| `GET /health` | For the server's own monitoring. |
