using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AyDisk.Relay;

/// <summary>
/// Optional per-technician access tokens supplied only through a server environment variable.
/// Missing configuration permits local prototype testing; do not expose that mode to public networks.
/// </summary>
public sealed class TechnicianRegistry
{
    private readonly Dictionary<string, byte[]> _tokens = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEnabled { get; }

    public TechnicianRegistry(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        IsEnabled = true;
        Dictionary<string, string>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid AYDISK_TECHNICIANS_JSON configuration.", ex);
        }

        if (entries == null || entries.Count == 0)
            throw new InvalidOperationException("AYDISK_TECHNICIANS_JSON must contain at least one technician.");

        foreach (var (name, token) in entries)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 ||
                string.IsNullOrWhiteSpace(token) || token.Length < 32 ||
                !_tokens.TryAdd(name.Trim(), SHA256.HashData(Encoding.UTF8.GetBytes(token))))
                throw new InvalidOperationException("Invalid or duplicate technician identifier/token.");
        }
    }

    public bool IsAuthorized(string? technicianId, string? technicianToken)
    {
        if (!IsEnabled)
            return true; // Explicitly local prototype mode only.

        if (string.IsNullOrWhiteSpace(technicianId) || string.IsNullOrWhiteSpace(technicianToken) ||
            !_tokens.TryGetValue(technicianId.Trim(), out var expected))
            return false;

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(technicianToken));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
