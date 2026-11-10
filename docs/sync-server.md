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
  password, and the server signs every other PC out. The server keeps only a hash of a check made from it. With
  two-factor on, a code from the phone (or a recovery code) is needed as well, so the recovery key alone isn't
  enough. Each reset makes a new recovery key, shown once; the old one stops working.
- **Invite codes** have two halves: the server's half (it knows the invite) and a secret half that unlocks the
  workspace key and is never sent to the server. Joining also brings a check made from the secret half, which must
  match the one kept (hashed) with the invite, so the server's half alone doesn't let anyone in. Give the whole
  code to your partner privately.
- **Two-factor** with any authenticator app (RFC 6238 codes, `Totp` in Core). Each code works once. Turning it on
  gives eight one-time recovery codes, stored hashed. The secret is kept sealed with the server's own key.
- **Wrong tries:** ten wrong passwords from one address keep that address out of the account for 15 minutes, so
  someone guessing from elsewhere can't lock the owner out; a hundred from everywhere together lock the whole
  account for 15 minutes. They're counted in the database, so tries sent at once all count, and each address can
  try signing in at most 20 times a minute. Turning two-factor off signs out
  every other device.
- **Devices:** each signed-in PC has a 15-minute access token and a 30-day refresh token. A refresh token works
  once; using any earlier one again, however long ago, signs that PC out, as someone may have copied it. After 90
  days a PC signs in again with the password, however often it refreshed.
- **No giveaways:** asking for the salt of an email with no account gives a made-up one that's the same every
  time, and signing in to one takes as long as a real check. Signing up with an email that's taken does say so.
- **Room:** each request is at most 8 MB, each workspace keeps at most 500 MB and 200 000 records, a pull hands back
  at most 8 MB, and at most 20 new workspaces a day are made (`Webignite:SignUpsPerDay`), so nobody can fill the
  server.
- **HTTPS only:** the app refuses a server address that isn't `https://` (except one on the same PC, for testing).
  On a real server, Caddy in front of it does HTTPS; the sync server only listens on the machine itself.

## How shared projects sync

- **Locked before they leave.** Each record (a project, section, task, comment, activity line, message, or who's who) is
  turned into JSON on the PC and locked with AES-256-GCM, using a key made from the workspace key
  (`RecordLock` in Core). The record's kind and id are bound into the lock, so the server can't swap one record
  for another or change one unnoticed: a record that doesn't open is left out and counted.
- **What the server sees:** the workspace, each record's kind and id, its size, and the order records arrived in.
  Never names, titles, notes or anything inside.
- **Only shared projects.** Projects ticked Shared, with their sections, tasks, comments, activity and messages. Personal
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

## How quotes, invoices, clients and contacts sync

- **All of them.** The business is run by both of you, so every quote, invoice, client and person in Contacts syncs,
  not only what's ticked. They're locked the same way as projects (`RecordLock`), as `Shared<T>`: the record, when
  it was last changed and, for one deleted, when (`SyncService.Shared.cs`). Mail and site logins stay on each PC.
- **Merging** (`SharedMerge` in Core). The copy changed later wins, with care so nothing that happened is lost: once
  a quote or invoice is approved, emailed, chased, settled or taken off To approve on either PC it stays so; the link
  to Xero keeps whichever copy got further with Xero; credit notes and attachments from both PCs are kept. A deletion
  stands unless the other PC changed the record after it. Deletions are noted (table `sync_gone`) so they reach the
  other PC.
- **What stays on the PC.** A client's folder picked by hand and its linked files are paths on that PC: they never
  leave it, and a client arriving from the other PC keeps this PC's own.
- **Xero.** Each of you connects Xero on your own PC (the same organisation). Clients, quotes and invoices brought in
  from Xero get ids made from Xero's (`xc-…`, `xd-…`), so both PCs bringing in the same one keep it as one; and a PC
  signed in but not synced yet waits before bringing anything in from Xero (`AppServices.WaitsForFirstSync`).

## How the chat travels

- **Messages** (`ChatMessage` in Core, kind `chat`) are written once and never changed, so they're sent once and kept
  as they come; nothing to merge. Locked with the workspace key like everything else.
- **Files and screenshots** (at most 10 MB each, `ChatRules`) travel in parts of 256 KB (kind `chatpart`), so each
  locked record stays well under the server's limit. A file shows as "On its way…" until all its parts are in.
- **Unread.** Each PC notes when the chat was last read (setting `chat-read`) and which messages it has already
  announced (setting `chat-announced`), so a note pops up once per new message.

## Licences

A workspace's plan is on the server (`trial`, `active` with a paid-until day, or `complimentary`); `set-plan`
changes it. The server signs the licence with ECDSA P-256 (`Licences` in Core); the app checks it with the public
key built in (`LicenceKey.Public`), keeps it in the setting `licence` and fetches it again twice a day while
signed in. Each time it's read, the licence must be for the workspace signed in, and the PC's clock mustn't be
behind the latest time already seen (setting `licence-latest-seen`), so winding the clock back doesn't help. The trial is 30 days; after the licence runs out there are 14 days' grace, then the app locks
(`Licences.Locks`, not switched on until payments are set up).

## Settings

| Environment variable | What it is |
| --- | --- |
| `WEBIGNITE_SERVER_SECRET` | 32 or more random bytes, Base64 (`openssl rand -base64 32`). Required outside Development. It seals two-factor secrets and makes the stand-in salts. **Never put it in the repository**; losing it means everyone sets up two-factor again. |
| `WEBIGNITE_SERVER_DATA` | The folder for `webignite-server.db` (default `./data`). Back this folder up. |
| `WEBIGNITE_LICENCE_KEY` | The key licences are signed with (Base64 PKCS#8), from `make-licence-key`. Without it, licences aren't offered. |

## Trying it on this PC

```
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Webignite.Server --urls http://localhost:5080
```

Then in Webignite, Settings, Account and sync, use `http://localhost:5080` as the sync server.

## On a real server

On the Synology NAS at home, all in DSM with no commands: [synology-setup.md](synology-setup.md) (`deploy/synology/compose.yaml`, with the ready-made image `ghcr.io/edwebignite/webignite-server`). There the server keeps its own secret and licence key as files in its data folder (`ServerFiles`, readable by the server alone; settings, when given, win) and copies its database every night at 02:30 into `data/backups`, keeping 30 days (`NightlyBackup`).

Step by step on a hosted server you fully control (xneelo Cloud, not a managed server), with Docker, Caddy for HTTPS, a firewall, nightly backups and updates: [server-setup.md](server-setup.md). The files for it are in `deploy/`.

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
| `POST /api/account/password` | A new password while signed in, proved with the current one; every other device is signed out. |
| `POST /api/sessions/others/sign-out` | Signs out every other device of the account. |
| `POST /api/workspace/members/remove` | The owner takes someone out: their account and devices go, and open invites are cancelled. What they synced before stays on their PC; the workspace key isn't changed. |
| `POST /api/sync/push` | Locked records for the caller's workspace (`{records: [{kind, id, payload}]}`); each gets the next number. Returns the last number. |
| `POST /api/sync/pull` | Locked records numbered after `since`, oldest first, up to 500, with the new cursor and whether there are more. |
| `GET /api/licence` | The workspace's licence, signed: a 30-day trial from when it was made, paid until a day, or free. |
| `GET /health` | For the server's own monitoring. |
