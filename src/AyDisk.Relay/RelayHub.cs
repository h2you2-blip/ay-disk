using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;

namespace AyDisk.Relay;

/// <summary>
/// Relay between Host (the computer being supported) and Viewer (the technician).
/// Flow: Host registers -> gets 9-digit ID. Viewer requests session with ID + password.
/// Host user must click "Allow". Then frames go Host -> Viewer and input goes Viewer -> Host.
/// </summary>
public class RelayHub : Hub
{
    private record HostInfo(string ConnectionId, byte[] PasswordHash, string MachineName);

    // hostId -> host info
    private static readonly ConcurrentDictionary<string, HostInfo> Hosts = new();
    // host connectionId -> hostId
    private static readonly ConcurrentDictionary<string, string> HostConnToId = new();
    // viewer connectionId -> host connectionId (waiting for host approval)
    private static readonly ConcurrentDictionary<string, string> Pending = new();
    // active sessions (one viewer per host)
    private static readonly ConcurrentDictionary<string, string> HostToViewer = new();
    private static readonly ConcurrentDictionary<string, string> ViewerToHost = new();
    // Keep session admission/removal atomic across concurrent SignalR calls.
    private static readonly object SessionSync = new();
    // brute-force protection: hostId -> (failures, lockedUntil)
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> Failures = new();

    private readonly ILogger<RelayHub> _log;
    private readonly TechnicianRegistry _technicians;
    public RelayHub(ILogger<RelayHub> log, TechnicianRegistry technicians)
    {
        _log = log;
        _technicians = technicians;
    }

    // ---------------- HOST ----------------

    public string RegisterHost(string machineName, string password, string preferredId)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new HubException("Password must be at least 6 characters.");

        string id = IsValidId(preferredId) &&
                    (!Hosts.TryGetValue(preferredId, out var existing) || existing.ConnectionId == Context.ConnectionId)
            ? preferredId
            : NewId();

        Hosts[id] = new HostInfo(Context.ConnectionId, Hash(password), machineName);
        HostConnToId[Context.ConnectionId] = id;
        _log.LogInformation("Host {Machine} registered as {Id}", machineName, id);
        return id;
    }

    public async Task<bool> RespondToSession(string viewerConnectionId, bool accepted)
    {
        bool started = false;
        lock (SessionSync)
        {
            // An unrelated client must never consume another host's pending request.
            if (!Pending.TryGetValue(viewerConnectionId, out var hostConn) ||
                hostConn != Context.ConnectionId)
                return false;

            Pending.TryRemove(viewerConnectionId, out _);
            if (accepted && HostConnToId.ContainsKey(hostConn) &&
                !HostToViewer.ContainsKey(hostConn) &&
                !ViewerToHost.ContainsKey(viewerConnectionId))
            {
                HostToViewer[hostConn] = viewerConnectionId;
                ViewerToHost[viewerConnectionId] = hostConn;
                started = true;
            }
        }

        if (!started)
        {
            await Clients.Client(viewerConnectionId).SendAsync("SessionRejected",
                accepted ? "This session is no longer available." : "The remote user declined the connection.");
            return false;
        }

        await Clients.Client(viewerConnectionId).SendAsync("SessionAccepted");
        _log.LogInformation("Session started host={Host} viewer={Viewer}",
            Context.ConnectionId, viewerConnectionId);
        return true;
    }

    public async Task SendFrame(byte[] jpeg, int width, int height)
    {
        // Bound untrusted frame metadata and payload before relaying.
        if (jpeg == null || jpeg.Length == 0 || jpeg.Length > 6 * 1024 * 1024 ||
            width < 1 || height < 1 || width > 16384 || height > 16384)
            throw new HubException("Invalid frame.");

        if (HostToViewer.TryGetValue(Context.ConnectionId, out var viewer))
            await Clients.Client(viewer).SendAsync("Frame", jpeg, width, height);
    }

    // ---------------- VIEWER ----------------

    public async Task<string> RequestSession(string hostId, string password, string viewerName, string technicianId, string technicianToken)
    {
        hostId = (hostId ?? "").Replace(" ", "");

        if (!_technicians.IsAuthorized(technicianId, technicianToken))
        {
            _log.LogWarning("Rejected unauthenticated session request connection={Connection}", Context.ConnectionId);
            return "Technician sign-in failed.";
        }

        if (Failures.TryGetValue(hostId, out var f) && f.LockedUntil > DateTime.UtcNow)
            return "Too many wrong passwords. Try again in a few minutes.";

        if (!Hosts.TryGetValue(hostId, out var host))
            return "Host not found or offline.";

        if (!CryptographicOperations.FixedTimeEquals(host.PasswordHash, Hash(password ?? "")))
        {
            Failures.AddOrUpdate(hostId, (1, DateTime.MinValue), (_, old) =>
                old.Fails + 1 >= 5 ? (0, DateTime.UtcNow.AddMinutes(5)) : (old.Fails + 1, old.LockedUntil));
            return "Wrong password.";
        }
        Failures.TryRemove(hostId, out _);

        lock (SessionSync)
        {
            if (!HostConnToId.ContainsKey(host.ConnectionId) ||
                HostToViewer.ContainsKey(host.ConnectionId) ||
                Pending.ContainsKey(Context.ConnectionId) ||
                Pending.Values.Contains(host.ConnectionId))
                return "Host is busy or request already pending.";

            Pending[Context.ConnectionId] = host.ConnectionId;
        }
        var displayName = _technicians.IsEnabled
            ? technicianId.Trim()
            : (viewerName ?? "Technician");
        displayName = displayName[..Math.Min(displayName.Length, 80)];
        _log.LogInformation("Technician {Technician} requested host {Host}", displayName, hostId);
        await Clients.Client(host.ConnectionId).SendAsync("SessionRequested",
            Context.ConnectionId, displayName);
        return "PENDING";
    }

    public async Task SendInput(string type, double x, double y, int button, int delta, int keyCode)
    {
        if (ViewerToHost.TryGetValue(Context.ConnectionId, out var host))
            await Clients.Client(host).SendAsync("Input", type, x, y, button, delta, keyCode);
    }

    // ---------------- BOTH ----------------

    public Task EndSession() => EndSessionFor(Context.ConnectionId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var conn = Context.ConnectionId;
        await EndSessionFor(conn);

        List<string> cancelled = new();
        lock (SessionSync)
        {
            Pending.TryRemove(conn, out _);
            foreach (var pending in Pending.Where(x => x.Value == conn).ToArray())
            {
                if (Pending.TryRemove(pending.Key, out _))
                    cancelled.Add(pending.Key);
            }

            if (HostConnToId.TryRemove(conn, out var id) &&
                Hosts.TryGetValue(id, out var info) && info.ConnectionId == conn)
                Hosts.TryRemove(id, out _);
        }
        foreach (var viewer in cancelled)
            await Clients.Client(viewer).SendAsync("SessionRejected", "Host disconnected.");

        await base.OnDisconnectedAsync(exception);
    }

    private async Task EndSessionFor(string conn)
    {
        string? peer = null;
        lock (SessionSync)
        {
            Pending.TryRemove(conn, out _);
            if (HostToViewer.TryRemove(conn, out var viewer))
            {
                ViewerToHost.TryRemove(viewer, out _);
                peer = viewer;
            }
            else if (ViewerToHost.TryRemove(conn, out var host))
            {
                HostToViewer.TryRemove(host, out _);
                peer = host;
            }
        }
        if (peer != null)
        {
            await Clients.Client(peer).SendAsync("SessionEnded");
            _log.LogInformation("Session ended connection={Connection}", conn);
        }
    }

    // ---------------- helpers ----------------

    private static byte[] Hash(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    private static bool IsValidId(string? id) => id is { Length: 9 } && id.All(char.IsDigit);

    private static string NewId()
    {
        string id;
        do { id = RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString(); }
        while (Hosts.ContainsKey(id));
        return id;
    }
}
