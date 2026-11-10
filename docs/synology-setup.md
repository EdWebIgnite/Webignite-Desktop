# Setting up the sync server on the Synology NAS

This puts the Webignite sync server on the Synology NAS at home, so both PCs can sync from anywhere. It's all
clicking in DSM and in the router, no commands to type. Plan on about an hour.

What you end up with:

- The sync server running on the NAS in Container Manager, starting by itself whenever the NAS starts.
- An address for the apps, like `https://webignite-sync.synology.me`, with a proper HTTPS certificate that
  renews by itself.
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

## 4. Make a folder for Webignite

1. Open **File Station**.
2. Click the **docker** folder, then **Create**, **Create folder**, and call it `webignite`.

## 5. Start the sync server

1. Open **Container Manager** and click **Project** on the left.
2. Click **Create**.
3. **Project name:** `webignite`
4. **Path:** click **Set Path**, choose **docker**, then **webignite**, and click **Select**.
5. **Source:** choose **Create docker-compose.yml**.
6. Copy everything in
   [deploy/synology/compose.yaml](https://github.com/EdWebIgnite/Webignite-Desktop/blob/main/deploy/synology/compose.yaml)
   (the copy button at the top right of that page copies it all) and paste it into the big box.
7. Click **Next**. If it asks about a web portal through Web Station, leave that **unticked**. Click **Next**
   again, then **Done**.

It downloads the server and starts it. When it's finished, the project shows two containers:

- **webignite-server-1** running, with a green dot. That's the sync server.
- **webignite-prepare-1** stopped. That's right: it only gets the folder ready, then stops.

## 6. Get an address for the NAS

1. In DSM, open **Control Panel**, **External Access**, **DDNS**, and click **Add**.
2. **Service provider:** Synology.
3. **Hostname:** type `webignite-sync` and keep `synology.me` next to it. If that name is taken, try another,
   such as `webignite-eddie`. Write down the full address, for example `webignite-sync.synology.me`.
4. Tick **Get a certificate from Let's Encrypt and set it as default**.
5. Tick **Enable Heartbeat** if it's there.
6. Click **OK**. It takes a minute to get the certificate.

The rest of this guide uses `webignite-sync.synology.me`. Use your own address wherever you see it.

## 7. Send the address to the sync server

1. In DSM, open **Control Panel**, **Login Portal**, the **Advanced** tab, and click **Reverse Proxy**.
2. Click **Create** and fill it in like this:

   | | |
   | --- | --- |
   | Reverse proxy name | `Webignite sync` |
   | **Source:** Protocol | HTTPS |
   | **Source:** Hostname | `webignite-sync.synology.me` |
   | **Source:** Port | `443` |
   | Enable HSTS | ticked |
   | **Destination:** Protocol | HTTP |
   | **Destination:** Hostname | `localhost` |
   | **Destination:** Port | `5080` |

3. Click **Save**.
4. Open **Control Panel**, **Security**, **Certificate**, and click **Settings**. Next to
   `webignite-sync.synology.me`, choose the certificate for `webignite-sync.synology.me`, and click **OK**.

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
4. If **Control Panel**, **Security**, **Firewall** is switched on, add a rule that allows port **443**.

## 10. Check it works

1. On your phone, **switch off Wi-Fi**, so it uses mobile data like someone outside your home.
2. Open `https://webignite-sync.synology.me/health`.

It should show `{"status":"ok"}`. Then open the same address on a PC at home. It should show the same.

If it doesn't work, see Troubleshooting at the end.

## 11. Send me the licence key

Open `https://webignite-sync.synology.me/licence-public-key`. It shows one long line of letters and numbers.
Copy all of it and send it to me. It's safe to share, and it goes into the app so licences work.

## 12. Sign up in Webignite

On your PC, in Webignite, open **Settings**, **Account and sync**:

1. **Sync server:** `https://webignite-sync.synology.me`
2. **Create an account** with your email, a long password and the workspace name (Webignite).
3. Write down or print the **recovery key** it shows, and keep it away from the PC. It's the only way back in
   if you forget your password.
4. **Turn on two-factor** with your authenticator app, and keep the recovery codes with the recovery key.
5. **Make an invite code** and send it to your partner somewhere only they can see it.

On your partner's PC: the same sync server address, then **Join with an invite**, their own email and password,
and the code. They keep their own recovery key and turn on two-factor too.

Projects ticked **Shared** then sync every minute while you're both signed in.

## 13. Back up somewhere other than the NAS

The server copies its database every night at 02:30 into `docker/webignite/data/backups` and keeps 30 days.
That doesn't help if the NAS itself fails, so copy the whole `webignite` folder somewhere else as well:

1. In **Package Center**, install **Hyper Backup**.
2. Open it, click **+**, **Data backup task**.
3. Choose where it goes: a USB drive plugged into the NAS, or a cloud such as Synology C2 or Google Drive.
4. Tick the folder **docker**, **webignite**.
5. Set it to run **daily** at **03:30**.
6. Switch on **encryption** when it asks, and keep that password in your password manager.

That folder also holds the server's own secret and licence key, so keep the backup private.

## Keeping it up to date

When the sync server changes, I'll tell you. Then:

1. Open **Container Manager**, **Image**.
2. **ghcr.io/edwebignite/webignite-server** shows **Update available**. Click it, then **Update**.

It downloads the new version and restarts the server. Sync pauses for a minute and carries on.

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
  **webignite**, click **Action**, **Start**.
- **A certificate warning:** redo step 7, part 4, and pick the `synology.me` certificate.
- **Anything else:** in Container Manager, **Container**, **webignite-server-1**, **Log**. Copy the last lines
  and send them to me.

## Later, when Webignite is sold to clients

Clients' sync should then run on a proper hosted server, not on a NAS at home. The app only needs a new sync
server address for that. [server-setup.md](server-setup.md) has the steps for a hosted server.
