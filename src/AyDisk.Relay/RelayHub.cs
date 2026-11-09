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
    // brute-force protection: hostId -> (failures, lockedUntil)
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> Failures = new();

    private readonly ILogger<RelayHub> _log;
    public RelayHub(ILogger<RelayHub> log) => _log = log;

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

    public async Task RespondToSession(string viewerConnectionId, bool accepted)
    {
        // Only the host that was asked can answer
        if (!Pending.TryRemove(viewerConnectionId, out var hostConn) || hostConn != Context.ConnectionId)
            return;

        if (!accepted)
        {
            await Clients.Client(viewerConnectionId).SendAsync("SessionRejected", "The remote user declined the connection.");
            return;
        }

        HostToViewer[hostConn] = viewerConnectionId;
        ViewerToHost[viewerConnectionId] = hostConn;
        await Clients.Client(viewerConnectionId).SendAsync("SessionAccepted");
        _log.LogInformation("Session started host={Host} viewer={Viewer}", hostConn, viewerConnectionId);
    }

    public async Task SendFrame(byte[] jpeg, int width, int height)
    {
        if (HostToViewer.TryGetValue(Context.ConnectionId, out var viewer))
            await Clients.Client(viewer).SendAsync("Frame", jpeg, width, height);
    }

    // ---------------- VIEWER ----------------

    public async Task<string> RequestSession(string hostId, string password, string viewerName)
    {
        hostId = (hostId ?? "").Replace(" ", "");

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

        if (HostToViewer.ContainsKey(host.ConnectionId))
            return "Host is already in a session.";

        Pending[Context.ConnectionId] = host.ConnectionId;
        await Clients.Client(host.ConnectionId).SendAsync("SessionRequested", Context.ConnectionId, viewerName);
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
        Pending.TryRemove(conn, out _);

        if (HostConnToId.TryRemove(conn, out var id) &&
            Hosts.TryGetValue(id, out var info) && info.ConnectionId == conn)
            Hosts.TryRemove(id, out _);

        await base.OnDisconnectedAsync(exception);
    }

    private async Task EndSessionFor(string conn)
    {
        if (HostToViewer.TryRemove(conn, out var viewer))
        {
            ViewerToHost.TryRemove(viewer, out _);
            await Clients.Client(viewer).SendAsync("SessionEnded");
        }
        else if (ViewerToHost.TryRemove(conn, out var host))
        {
            HostToViewer.TryRemove(host, out _);
            await Clients.Client(host).SendAsync("SessionEnded");
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
