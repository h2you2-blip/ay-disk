# AY Disk relay on a VPS (safe staging template)

This template is **loopback-only**. It is meant to validate the Relay process on your VPS without exposing an unauthenticated remote-control service to the public internet.

## Requirements

- A Linux VPS with Docker Engine and Docker Compose plugin
- SSH access to the VPS
- No public port forwarding for the relay

## Launch

From the repository root:

```bash
docker compose -f deploy/relay/compose.yml up -d --build
curl --fail http://127.0.0.1:5000/
docker compose -f deploy/relay/compose.yml logs --tail=50
```

Expected response: `AyDisk relay is running`.

To stop:

```bash
docker compose -f deploy/relay/compose.yml down
```

The `127.0.0.1:5000:5000` binding intentionally keeps the Relay unreachable from the internet. Do **not** change it to `0.0.0.0` or publish port 5000.

## What must happen before public deployment

1. Implement authenticated client/server identities (not just a shared Host ID and password), session-bound authorization and appropriate rate limits.
2. Set up a trusted HTTPS reverse proxy and valid TLS certificates; require TLS for all external connections.
3. Add persistent security audit records, secret/key management, operational monitoring and an incident response procedure.
4. Test Host/Viewer behavior on two Windows PCs, including rejection, disconnection, concurrent requests, password failures and reconnects.
5. Define retention and privacy policies for any remote-support logs or session data.

**The current application is an attended prototype and should not be offered as a production/public remote-access service yet.**
