# AY Disk — Quick Start

Use this guide to test the **attended remote-support prototype**. The receiving user must approve the session on the Host every time.

## Download the compiled build

1. Sign in to GitHub and open [AY Disk Actions](https://github.com/h2you2-blip/ay-disk/actions).
2. Open the latest **successful** run of **Build AY Disk**.
3. Under **Artifacts**, download **AY-Disk** and extract the ZIP.
4. Keep the extracted `Host`, `Viewer`, `Relay-Windows`, and `Relay-Linux` directories intact.

## First test — one Windows computer (no internet server required)

1. Run `Relay-Windows/AyDisk.Relay.exe` and leave its console window open.
2. Open `http://127.0.0.1:5000/` in the browser. It should display `AyDisk relay is running`.
3. Run `Host/AyDisk.Host.exe`. Set **Relay server URL** to `http://127.0.0.1:5000` and select **Go online**.
4. Write down the **9-digit ID** and **temporary password** displayed by Host.
5. Run `Viewer/AyDisk.Viewer.exe`. Enter the same relay URL, Host ID and password, then select **Connect**.
6. Return to Host and select **Yes** on the request to allow viewing and control.
7. Confirm the screen appears in Viewer. Select **End session** in Host to verify immediate termination.

The self-contained Windows executables do not need the .NET runtime installed. Windows may display a SmartScreen warning for an unsigned development build; only run binaries you trust and have verified from your own GitHub Actions run.

## Next test — two Windows PCs on a trusted private LAN

Run the Relay on a Windows computer that both PCs can reach. On that computer, open PowerShell in `Relay-Windows` and run:

```powershell
.\AyDisk.Relay.exe --urls http://192.168.1.100:5000
```

**Replace `192.168.1.100` with the actual private IPv4 address of the relay computer.** Allow TCP 5000 only from the trusted private LAN in Windows Firewall. On the Host and Viewer PCs, set Relay server URL to `http://192.168.1.100:5000` (using your actual address). They can then use the Host ID, password, and explicit Allow/Deny prompt.

**Security:** This LAN example uses unencrypted HTTP and is for testing on a trusted, isolated network only. Do not expose TCP 5000 to the public internet, do not port-forward it, and do not use it on an untrusted Wi-Fi network. To test over the internet, first configure HTTPS/WSS via a reverse proxy with a valid certificate, and harden relay authentication and access controls.

## Linux relay for a future server test

The artifact also contains `Relay-Linux/AyDisk.Relay` — a self-contained executable for Linux x64. On a Linux machine:

```bash
chmod +x ./AyDisk.Relay
./AyDisk.Relay --urls http://127.0.0.1:5000
```

This binds only to loopback. Do not make it publicly reachable before proper server security, authentication, TLS, and logging are in place.

## What this build does **not** guarantee

- A successful CI smoke test confirms the Relay process starts and its health endpoint responds; it is **not** an end-to-end screen/control test.
- A real Windows Host/Viewer test is still required to verify approval, frame display, mouse/keyboard input, disconnection and reconnect.
- This is an early, attended prototype. It is **not** ready for unattended, high-risk or production-wide remote access.
