# Setting up the sync server on the Synology NAS

This puts the Webignite sync server on the Synology NAS at home, so both PCs can sync from anywhere. It's all
clicking in DSM and in the router, no commands to type. Plan on about an hour.

What you end up with:

- The sync server running on the NAS in Container Manager, starting by itself whenever the NAS starts.
- An address for the apps, like `https://sync.yourname.synology.me`, with a proper HTTPS certificate that
  renews by itself. Webignite has Webignite's own address built in, so people joining only need an invite code.
- A backup of the server's database every night, kept for 30 days.

Everything that syncs is locked on your PCs before it leaves, so the NAS never holds anything it can read.

If a screen looks a little different from what's written here, look for the same words nearby.

## 1. Check your home internet can be reached

Some internet providers share one internet address between many homes. Then nothing from outside can reach the
NAS, and this guide won't work as it is. It takes two minutes to check:

1. Log in to your router (usually by typing `192.168.1.1` or `192.168.0.1` into your browser).
2. Find the router's **internet address**. It's usually on the first page or under Status, and is called
   **WAN IP**, **Internet IP** or **Public IP**. Write it down.
3. On a PC at home, open [whatismyipaddress.com](https://whatismyipaddress.com) and look at the **IPv4** address.

If the two are the same, carry on. If they're different, or the router's starts with `100.`, stop here and
let me know: there's another way, it just needs one extra free app on each PC.

## 2. Give the NAS a fixed address at home

The router needs to know where the NAS is, and that address shouldn't change.

1. In DSM, open **Control Panel**, **Network**, **Network Interface**. Click **LAN 1** (or the one that says
   Connected) and note its **IP address**, for example `192.168.1.20`.
2. In the router, find **DHCP reservation**, **Address reservation** or **Static lease** (often under LAN or
   DHCP settings). Add the NAS with that address, so the router always gives it the same one.

If you can't find that setting in the router, tell me the router's make and model.

## 3. Install Container Manager

1. In DSM, open **Package Center**.
2. Search for **Container Manager** and click **Install**.

This also makes a shared folder called **docker**.

## 4. Make the folders for the sync server

1. Open **File Station**.
2. Click the **docker** folder, then **Create**, **Create folder** (not Create shared folder), and call it
   `system_sync`.
3. Open **system_sync**, and in it make one more folder called `data`. The NAS doesn't make it by itself.

## 5. Start the sync server

1. Open **Container Manager** and click **Project** on the left.
2. Click **Create**.
3. **Project name:** `system_sync`
4. **Path:** click **Set Path**, choose **docker**, then **system_sync**, and click **Select**.
5. **Source:** choose **Create docker-compose.yml**.
6. Copy everything in
   [deploy/synology/compose.yaml](https://github.com/EdWebIgnite/Webignite-Desktop/blob/main/deploy/synology/compose.yaml)
   (the copy button at the top right of that page copies it all) and paste it into the big box.
7. Click **Next**. If it asks about a web portal through Web Station, leave that **unticked**. Click **Next**
   again, then **Done**.

It downloads the server and starts it. When it's finished, the project shows **system_sync-server-1** running,
with a green dot. Its **Log** tab shows **Application started**.

## 6. Get an address and a certificate for the NAS

1. In DSM, open **Control Panel**, **External Access**, **DDNS**. If a **Synology** address is already listed
   with status **Normal** (for example `yourname.synology.me`), use that one: a NAS can only have one. If not,
   click **Add**, choose **Synology**, pick a hostname, and click **OK**.
2. The sync server gets its own part of that address: `sync.yourname.synology.me`. Synology points every such
   part at your NAS already.
3. Open **Control Panel**, **Security**, **Certificate**, and click **Add**, **Add a new certificate**, **Next**.
   Description `Sync`, choose **Get a certificate from Let's Encrypt**, **Next**:
   - **Domain name:** `yourname.synology.me`
   - **Email:** your email (only used to warn you if renewal fails)
   - **Subject Alternative Name:** `*.yourname.synology.me` (with the star and dot, so it covers `sync.`)
   - leave **Set as default certificate** unticked, and click **Done**.

The rest of this guide uses `sync.yourname.synology.me`. Use your own address wherever you see it.

## 7. Send the address to the sync server

1. In DSM, open **Control Panel**, **Login Portal**, the **Advanced** tab, and click **Reverse Proxy**.
2. Click **Create** and fill it in like this:

   | | |
   | --- | --- |
   | Reverse proxy name | `Sync` |
   | **Source:** Protocol | HTTPS |
   | **Source:** Hostname | `sync.yourname.synology.me` |
   | **Source:** Port | `443` |
   | Enable HSTS | ticked |
   | **Destination:** Protocol | HTTP |
   | **Destination:** Hostname | `localhost` |
   | **Destination:** Port | `5080` |

3. Click **Save**.
4. Open **Control Panel**, **Security**, **Certificate**, and click **Settings**. Scroll to the bottom of the
   list: next to `sync.yourname.synology.me`, choose the `yourname.synology.me` certificate, and click **OK**.

## 8. Open the door in the router

In the router, find **Port forwarding** (sometimes called **Virtual server** or **NAT**, often under Advanced or
Security). Add a rule:

| | |
| --- | --- |
| Name | `Webignite sync` |
| Protocol | TCP |
| Outside (external) port | `443` |
| Inside (internal) IP address | the NAS's address from step 2 |
| Inside (internal) port | `443` |

Save it. Only open port 443, nothing else. Don't open 5000 or 5001: those are DSM itself.

If the router says port 443 is already in use, the router's own remote management is probably using it. Switch
off remote management in the router and try again.

## 9. Keep DSM safe

The NAS can now be reached from the internet, so make sure these are on:

1. **Control Panel**, **Security**, **Protection**: tick **Enable auto block**. The suggested settings are fine.
2. **Control Panel**, **Security**, **Account**: tick **Enable Account Protection**.
3. Your DSM admin login has two-step sign-in on: click your name at the top right of DSM, **Personal**,
   **Security**, **2-Factor Authentication**.
4. If **Control Panel**, **Security**, **Firewall** is switched on and the check in step 10 fails from mobile
   data, add a rule that allows port **443**.

## 10. Check it works

1. On your phone, **switch off Wi-Fi**, so it uses mobile data like someone outside your home.
2. Open `https://sync.yourname.synology.me/health`.

It should show `{"status":"ok"}`. Then open the same address on a PC at home. It should show the same.

If it doesn't work, see Troubleshooting at the end.

## 11. The licence key

`https://sync.yourname.synology.me/licence-public-key` shows one long line of letters and numbers: the public
half of the server's licence key. It's safe to share. It goes into the app (`LicenceKey.Public`) so licences
work; for Webignite's own NAS it's in already.

## 12. Sign up in Webignite

On your PC, in Webignite, open **Settings**, **Account and sync**:

1. **Sync server:** Webignite's own is filled in. For another server, click **Change** and type its address.
2. **New account** with your email, a long password and the workspace name (Webignite).
3. Write down or print the **recovery key** it shows, and keep it away from the PC. It's the only way back in
   if you forget your password.
4. **Turn on two-factor** with your authenticator app, and keep the recovery codes with the recovery key.
5. **Make an invite code** and send it to your partner somewhere only they can see it.

On your partner's PC: **Join with an invite**, then the code, their own email and password. The sync server is
filled in already. They keep their own recovery key and turn on two-factor too.

Projects ticked **Shared** then sync every minute while you're both signed in.

## 13. Back up somewhere other than the NAS

The server copies its database every night at 02:30 into `docker/system_sync/data/backups` and keeps 30 days.
That doesn't help if the NAS itself fails, so copy the whole `system_sync` folder somewhere else as well:

1. In **Package Center**, install **Hyper Backup**.
2. Open it, click **+**, **Data backup task**.
3. Choose where it goes: a USB drive plugged into the NAS, or a cloud such as Synology C2 or Google Drive.
4. Tick the folder **docker**, **system_sync**.
5. Set it to run **daily** at **03:30**.
6. Switch on **encryption** when it asks, and keep that password in your password manager.

That folder also holds the server's own secret and licence key, so keep the backup private.

## Keeping it up to date

When the sync server changes, I'll tell you. Then:

1. Open **Container Manager**, **Image**.
2. **ghcr.io/edwebignite/webignite-server** shows **Update available**. Click it, then **Update**.

It downloads the new version and restarts the server. Sync pauses for a minute and carries on.

## Your own workspace free for good

A new workspace starts on a 30-day trial. To make yours free with no end date (for you and your partner):

1. In **Container Manager**, click **Container**, then the **system_sync-server-1** row.
2. Click **Action**, then **Open terminal**.
3. Click **Create**, then pick **bash**.
4. Type this, with your own email, and press Enter:
   `dotnet Webignite.Server.dll set-plan eddie@webignite.co.za complimentary`
5. It answers that the workspace is now on complimentary. Close the terminal.
6. In Webignite, click **Sync now** in Settings, Account and sync. Licence shows "Licensed, free of charge".

## Power cuts and the NAS being off

If the NAS is off or the internet is down, sync just waits. Nothing is lost: every PC keeps its own full copy
and catches up when the NAS is back. A small UPS for the NAS and the router keeps sync going through short power
cuts and protects the NAS's disks.

## Troubleshooting

- **The health page doesn't open on mobile data:** check the port forward in step 8 points at the NAS's address
  from step 2, and that the address in step 6 shows **Normal** under DDNS.
- **It opens on mobile data but not on a PC at home:** the router doesn't send your own address back inside your
  home. Tell me the router's make and model.
- **The health page says 502 or Bad Gateway:** the server isn't running. In Container Manager, **Project**,
  **system_sync**, click **Action**, **Start**.
- **A certificate warning:** redo step 7, part 4, and pick the `synology.me` certificate.
- **Anything else:** in Container Manager, **Container**, **system_sync-server-1**, **Log**. Copy the last lines
  and send them to me.

## Later, when Webignite is sold to clients

Clients' sync should then run on a proper hosted server, not on a NAS at home. The app only needs a new sync
server address for that. [server-setup.md](server-setup.md) has the steps for a hosted server.
