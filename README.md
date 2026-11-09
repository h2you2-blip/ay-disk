# AY Disk

**AY Disk** is a consent-based remote support application for Windows, built with C# and .NET 8. It consists of a desktop **Host**, a desktop **Viewer**, and a **SignalR Relay** server.

> Early prototype / proof of concept. Do not expose this version directly to the public internet without reviewing and hardening its authentication, encryption, authorization, logging and operational security.

## Components

| Project | Where it runs | Purpose |
| --- | --- | --- |
| `AyDisk.Host` | Windows PC receiving support | Generates an ID and temporary password; asks the user to approve incoming remote-control sessions |
| `AyDisk.Viewer` | Windows technician PC | Connects using the Host ID and password; displays the remote screen and sends keyboard/mouse input after approval |
| `AyDisk.Relay` | Reachable Windows/Linux server | SignalR relay for authenticated session requests, screen frames and input |

```text
Viewer -- input --> Relay -- input --> Host
Viewer <-- JPEG ---- Relay <-- JPEG ---- Host
```

## Requirements

- Visual Studio 2022 with the .NET desktop workload **or** the .NET 8 SDK to build.
- Windows 10/11 for the Host and Viewer. The Relay can run on Windows or Linux.
- A network connection to the same relay URL for Host and Viewer.

## Start here

Follow the [Quick Start guide](docs/QUICKSTART.md) to download the tested executables and try AY Disk locally before using a server.

## Run locally

Open `AyDisk.sln` in Visual Studio, configure multiple startup projects (Relay, Host, Viewer) and press **F5**.

Or use three terminals:

```powershell
dotnet run --project src/AyDisk.Relay
dotnet run --project src/AyDisk.Host
dotnet run --project src/AyDisk.Viewer
```

1. Start the Relay, which listens on `http://localhost:5000` by default.
2. Open **AY Disk Host**, enter `http://localhost:5000`, and select **Go online**.
3. Copy the displayed nine-digit ID and eight-character password.
4. Open **AY Disk Viewer**, enter the same relay URL, Host ID and password, then select **Connect**.
5. On the Host, approve the request. Screen sharing and remote input begin **only after approval**.
6. Use **End session** on Host or **Disconnect** on Viewer to stop.

## Running across a network

Use a publicly reachable or VPN-accessible Relay and configure both apps with the same URL. **Use HTTPS (`https://`) with a valid certificate outside localhost.** For example, put the Relay behind a reverse proxy that terminates TLS and supports WebSockets. Do not expose the plain HTTP listener to the internet: passwords and remote-session data would not have transport encryption.

## Build and download

A push to `main` triggers `.github/workflows/build.yml`, which smoke-tests the Relay startup and publishes:

- `Relay-Windows/` — standalone Windows x64 relay executable
- `Relay-Linux/` — standalone Linux x64 relay executable
- `Host/` — Windows x64 Host single-file executable
- `Viewer/` — Windows x64 Viewer single-file executable

In GitHub, go to **Actions → Build AY Disk → latest successful run → Artifacts → AY-Disk**. The workflow produces the downloadable artifact; executables are not committed to source control.

## Controls present in this prototype

- A temporary randomly generated password per Host launch, which can be regenerated.
- A **Host-side Allow / Deny** confirmation for incoming requests.
- Visible red **CONTROLLED by ...** state and an **End session** action.
- One active Viewer per Host, and a basic five-failure / five-minute lockout per Host ID.

## Known limitations

- Captures only the primary monitor; full JPEG frames can use substantial bandwidth.
- Cannot interact with UAC secure desktop, lock screen or Ctrl+Alt+Del.
- No end-to-end encryption, technician accounts, persistent session audit trail, enterprise management, file transfer or unattended access.
- This version is intended for **attended, explicitly authorized support**, not covert access.

## Roadmap

- [ ] Strong relay authentication, explicit authorization model and comprehensive audit logging
- [ ] Multi-monitor support, bandwidth optimization and clipboard sync
- [ ] Optional file transfer with user confirmation
- [ ] Technician SSO and enterprise policy management

## License

No license file is included with the supplied source. Do not assume third-party code is licensed for redistribution; review provenance before distributing a commercial build.
