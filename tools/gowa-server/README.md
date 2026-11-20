# Shared GOWA server

One PC runs GOWA and holds the single WhatsApp linked-device session; every other PC runs MochiDock in **Remote Server** mode and only polls it. That avoids a separate WhatsApp session (and QR scan) per PC, replayed old messages from a fresh session, and two copies fighting over one session.

## On the server PC (once)

1. Open an elevated PowerShell in this folder.
2. `.\install-gowa-server.ps1 -User gowa -Password (Read-Host -AsSecureString "Server password")`
   - copies `GOWA\windows-amd64.exe` to `C:\ProgramData\MochiDockGowaServer\gowa-server.exe` (renamed on purpose: MochiDock's local mode only ever stops its own GOWA, but a different name keeps them apart),
   - registers the startup task `MochiDock-GOWA-Server` (restarts if it stops) and the read-receipt relay task `MochiDock-GOWA-Relay`,
   - opens the port only for LAN and Tailscale addresses.
3. Open `http://localhost:3099`, log in, and scan the WhatsApp QR once (WhatsApp > Linked devices).
4. Do not run MochiDock in **Local** mode on this PC (it would start a second GOWA); use Remote here too if MochiDock runs on it.

Remove it again with `.\install-gowa-server.ps1 -Uninstall` (removes both tasks; the WhatsApp data in `C:\ProgramData\MochiDockGowaServer\data` is kept).

## On every PC

MochiDock > Settings > WhatsApp > Remote Server: `http://<server LAN or Tailscale IP>:3099`, the same user and password. The password is stored encrypted for your Windows user (`%LocalAppData%\MochiDock\gowa-remote.dat`), not in the settings file or exports.

## Read sync (clearing notifications that you read elsewhere)

GOWA only tells MochiDock that a message was read through a webhook (`message.ack`). In Remote mode MochiDock listens on port 47823 (Windows Firewall may ask once; allow private networks only) and accepts only requests that carry its secret token.

**The relay.** GOWA delivers a webhook to several addresses one after another and retries a dead one for a long time. With every dock listed directly, an offline dock (for example the main PC switched off) held up or lost the receipt for the other docks. So the installer gives GOWA exactly **one** address, a small loopback relay (`MochiDock-GOWA-Relay`, `tools\gowa-relay`). It answers at once and forwards each receipt to every dock **in parallel** (3 s timeout, one retry after 2 s, then dropped), so no dock can delay another. The docks are listed in `C:\ProgramData\MochiDockGowaServer\relay-targets.txt` (one address per line, re-read automatically) and every delivery is logged to `relay.log` in the same folder (host and status only; tokens are never logged).

1. Build the relay once: `scripts\build_and_publish.bat` does it, or run `tools\gowa-relay\publish.ps1` (output: `tools\gowa-relay\publish\MochiDock.GowaRelay.exe`, which the installer copies to the server PC).
2. On every PC open Settings > WhatsApp > Remote Server and copy the **Read sync** address.
3. On the server PC run the install script once with all of them:

`.\install-gowa-server.ps1 -User gowa -Password (Read-Host -AsSecureString "Server password") -WebhookUrls "http://100.x.x.x:47823/webhook?token=...","http://100.y.y.y:47823/webhook?token=..."`

Later, add or remove a PC without reinstalling or restarting anything:

`.\install-gowa-server.ps1 -AddWebhookUrl "http://100.z.z.z:47823/webhook?token=..."`

`.\install-gowa-server.ps1 -RemoveWebhookUrl "http://100.z.z.z:47823/webhook"` (the token part may be left out)

The addresses contain a secret, so do not share them. Check that it works in Settings > WhatsApp: the Read sync line shows "Last read receipt: HH:mm" and, if the server used a wrong token, how many calls were rejected. `relay.log` shows one line per dock and receipt (`... -> 200 in 12 ms`, or `failed` and then `dropped` for a dock that is off).

Without read sync, notifications still clear when you reply from any dock (the reply marks the chat read in WhatsApp), but not when you only read the message on the phone. Only a "read on another device" receipt (`read-self`) clears a notification; a contact reading your own message does not.

## Notes

- Open the dashboard (QR scan) only at `http://localhost:3099` on the server PC. Browsers hide `crypto.randomUUID` on plain `http://<ip>` pages, so the dashboard fails to start from another PC ("crypto.randomUUID is not a function"). MochiDock does not use the dashboard, so Remote mode over `http://<ip>:3099` works normally.
- Never forward port 3099 to the internet. Tailscale (`100.x.y.z`) or the LAN is the intended path.
- Each PC notifies independently from the one server; marking a chat read on one PC does not change the others.
- If a PC shows "Disconnected (login rejected)", the user or password is wrong; "unreachable" means the address or firewall; "no WhatsApp device linked" means the QR was not scanned.
- Logs: `%LocalAppData%\MochiDock\logs\mochidock.log` and `crash.log`.
