# Setting up the sync server on xneelo

This walks you through putting the Webignite sync server on a small xneelo Cloud server in Johannesburg, from
nothing to both PCs syncing. Plan on an hour or two the first time. You'll type commands into PowerShell on your
PC and into the server once you're logged in to it; copy them as they are.

What you end up with:

- An Ubuntu server at xneelo running two things in Docker: the sync server, and Caddy in front of it, which does
  HTTPS and renews its own certificate.
- `https://sync.webignite.co.za` (or whichever name you pick) for the apps to use.
- Nightly backups of the database, security updates installed by themselves, and a firewall that only lets in
  SSH and HTTPS.

Everything that syncs is locked on your PCs before it leaves, so the server never holds anything it can read. It
still holds your account details, so keep it locked down as below.

The names of buttons in the xneelo control panel may differ a little from what's written here; the steps are
the same.

## 1. Make an SSH key on your PC

You log in to the server with a key instead of a password. In **PowerShell** on your PC:

```
ssh-keygen -t ed25519 -C "eddie@webignite"
```

Press Enter to keep the suggested file. Type a passphrase when asked (it protects the key if your PC is ever
lost). Then show the public half, which is the part you give xneelo:

```
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

Copy the whole line that starts with `ssh-ed25519`. Never share the file without `.pub`.

## 2. Create the server at xneelo

In the xneelo control panel, under xneelo Cloud, create a new server (instance):

- **Image:** Ubuntu 24.04 LTS.
- **Size:** the smallest one with 2 GB of memory is plenty for the two of you. It's charged by the hour while it
  exists; check the current price on xneelo's pricing page.
- **SSH key:** paste the `ssh-ed25519` line from step 1.
- **Firewall or security group:** allow incoming TCP on ports **22** (SSH), **80** and **443** (web). Nothing else.

When it's running, note its **public IP address**.

## 3. Point a name at it

Add a DNS record for the name the apps will use. If webignite.co.za's DNS is at xneelo, it's in the control
panel under DNS for that domain:

- Type **A**, name **sync**, value **the server's public IP**.

It can take a few minutes to an hour to reach everyone. In PowerShell, this shows the IP once it has:

```
Resolve-DnsName sync.webignite.co.za
```

## 4. Log in and lock it down

In PowerShell (use the user name xneelo gives for the Ubuntu image, usually `ubuntu`):

```
ssh ubuntu@sync.webignite.co.za
```

Say yes to the fingerprint the first time. You're now typing on the server. Bring it up to date and add what's
needed:

```
sudo apt update && sudo apt upgrade -y
sudo apt install -y git sqlite3 ufw fail2ban unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
```

Answer **Yes**, so security updates install by themselves.

Turn on the firewall, letting in only SSH and the web:

```
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

Make sure nobody can log in with a password, only with your key:

```
echo "PasswordAuthentication no" | sudo tee /etc/ssh/sshd_config.d/90-keys-only.conf
sudo systemctl restart ssh
```

Before you close this window, open a **second** PowerShell window and check that `ssh ubuntu@sync.webignite.co.za`
still logs you in. If it does, you're safe to carry on.

## 5. Install Docker

```
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
```

Log out (`exit`) and back in, so the second line takes effect. `docker ps` should then answer without an error.

## 6. Get the server's code

```
sudo mkdir -p /opt/webignite && sudo chown $USER /opt/webignite
git clone https://github.com/EdWebIgnite/Webignite-Desktop.git /opt/webignite
cd /opt/webignite/deploy
```

When the repository is made private (before selling), the server needs its own read-only key to fetch it. Make one
with `ssh-keygen -t ed25519 -f ~/.ssh/webignite_deploy`, add the `.pub` half on GitHub under the repository's
Settings, Deploy keys (read-only), and change the address with
`git remote set-url origin git@github.com:EdWebIgnite/Webignite-Desktop.git`.

## 7. Settings and the server's secret

```
cp .env.example .env
openssl rand -base64 32
```

Copy the line `openssl` printed: it's the server's secret. Save a copy in your password manager now. Then open
the settings file:

```
nano .env
```

Set `SYNC_DOMAIN` to your name (`sync.webignite.co.za`, without `https://`) and paste the secret after
`WEBIGNITE_SERVER_SECRET=`. Save with Ctrl+O, Enter, and leave with Ctrl+X. Then make it readable by you alone:

```
chmod 600 .env
```

Losing the secret doesn't lose any data, but everyone has to set up two-factor again.

## 8. Start it

```
sudo mkdir -p data && sudo chown 1654:1654 data
docker compose up -d --build
```

The first build takes a few minutes. Then check, on the server and from your PC:

```
curl http://127.0.0.1:5080/health
curl https://sync.webignite.co.za/health
```

Both should say `{"status":"ok"}`. If the second one fails, give Caddy a minute to get its certificate, and see
Troubleshooting below.

## 9. Sign up in Webignite

On your PC, in Webignite, open **Settings, Account and sync**:

1. Sync server: `https://sync.webignite.co.za`.
2. **Create an account** with your email, a long password and the workspace name (Webignite).
3. Write down or print the **recovery key** it shows, and keep it away from the PC. It's the only way back in
   if you forget your password.
4. **Turn on two-factor** with your authenticator app, and keep the recovery codes with the recovery key.
5. **Make an invite code** and send it to your partner somewhere only they can see it.

On your partner's PC: the same server address, then **Join with an invite**, their own email and password, and
the code. They keep their own recovery key and turn on two-factor too.

Projects ticked **Shared** then sync every minute while you're both signed in.

## 10. Nightly backups

Have the server back up its database every night at 02:30. First install a copy of the backup script that only
root can change, so nothing in the repository's folder can change what runs as root:

```
sudo install -m 700 -o root -g root /opt/webignite/deploy/backup.sh /usr/local/sbin/webignite-backup
sudo crontab -e
```

Pick nano if it asks, and add this line at the end:

```
30 2 * * * WEBIGNITE_DEPLOY=/opt/webignite/deploy /usr/local/sbin/webignite-backup >> /var/log/webignite-backup.log 2>&1
```

When `backup.sh` changes in a later version, look at what changed and run the `install` line again.

Backups go to `/opt/webignite/deploy/backups`, readable by root only, and are kept for 30 days. A backup on the
same server doesn't help if the server is lost, so copy them off now and then. From PowerShell on your PC:

```
ssh ubuntu@sync.webignite.co.za "sudo install -d -o ubuntu -m 700 ~/backup-copies && sudo install -o ubuntu -m 600 /opt/webignite/deploy/backups/*.gz ~/backup-copies/"
scp "ubuntu@sync.webignite.co.za:backup-copies/*.gz" "$env:USERPROFILE\Documents\Webignite server backups\"
ssh ubuntu@sync.webignite.co.za "rm -rf ~/backup-copies"
```

(Make that folder first.) If xneelo offers snapshots of the server, a weekly one is a good extra.

## Licences

Each workspace gets a 30-day free trial when it's made. The server signs every licence with its own licence key,
and the app checks it with the public half built into the app, so it works offline and can't be made up. After
a licence runs out there are 14 days' grace, then Webignite will lock until it's renewed (the lock is switched
off until payments are set up). Payments through PayFast come later.

To set it up, once, on the server:

```
cd /opt/webignite/deploy
docker compose run --rm server make-licence-key
```

Put the **private key** it prints after `WEBIGNITE_LICENCE_KEY=` in `.env` (and in your password manager), then
`docker compose up -d`. Send me the **public key**: it goes into the app (`LicenceKey.Public`), and from that
build on Settings, Account and sync shows each workspace's licence.

To set a workspace's plan by hand, by the owner's email:

```
docker compose run --rm server set-plan eddie@webignite.co.za complimentary
docker compose run --rm server set-plan client@example.co.za active 2026-12-31
docker compose run --rm server set-plan client@example.co.za trial
```

`complimentary` is free for good (Webignite's own workspace), `active` is paid until the day given.

## Keeping it up to date

- **Security updates** for Ubuntu install by themselves. When `ls /var/run/reboot-required` finds the file,
  restart the server at a quiet time with `sudo reboot`; everything starts again by itself.
- **A new version of the sync server:** when a change to the server is merged, log in and run:

  ```
  /opt/webignite/deploy/update.sh
  ```

  It takes a backup first (with the installed copy of the backup script), rebuilds, restarts and checks that the
  server answers.

## Putting a backup back

Only if the database is lost or damaged. On the server:

```
cd /opt/webignite/deploy
docker compose stop server
ls backups
gunzip -c backups/webignite-server-2026-11-08-0230.db.gz > data/webignite-server.db
rm -f data/webignite-server.db-wal data/webignite-server.db-shm
sudo chown 1654:1654 data/webignite-server.db
docker compose start server
```

Use the name of the backup you want from the list. Anything synced after that backup comes back from your PCs on
their next sync, as they still have it.

## Troubleshooting

- **See what's happening:** `cd /opt/webignite/deploy && docker compose logs --tail 100`.
- **HTTPS doesn't work:** the name must point at this server (step 3) and ports 80 and 443 must be open both in
  xneelo's firewall and in `ufw`. `docker compose logs caddy` says what Let's Encrypt answered.
- **The server stops straight away:** `docker compose logs server`. "Set WEBIGNITE_SERVER_SECRET" means `.env`
  is missing the secret.
- **Webignite says it couldn't reach the sync server:** check `https://sync.webignite.co.za/health` in a browser.
- **Locked out of SSH:** use the console in the xneelo control panel to log in and fix it.

## What's on the server

| Where | What |
| --- | --- |
| `/opt/webignite` | This repository. |
| `/opt/webignite/deploy/.env` | The domain and the server's secret. Never shared, never in the repository. |
| `/opt/webignite/deploy/data` | The database (`webignite-server.db`). |
| `/opt/webignite/deploy/backups` | Nightly copies of it, 30 days. |
| `/opt/webignite/deploy/caddy-data` | Caddy's certificates. |

The files that make this up are in the repository's `deploy` folder: `Dockerfile` (the server's image),
`compose.yaml` (the server and Caddy, both on the server's own network so the sync server only listens on
127.0.0.1), `Caddyfile`, `backup.sh` and `update.sh`. How the server keeps accounts safe is in
[sync-server.md](sync-server.md).
