using System.Text.Json;

namespace AyDisk.Host;

/// <summary>Stores relay URL and this PC's ID so the ID stays the same (across launches).</summary>
public class HostSettings
{
    public string RelayUrl { get; set; } = "http://localhost:5000";
    public string HostId { get; set; } = "";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AY Disk", "host.json");

    public static HostSettings Load()
    {
        try { return JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
