using System.Text.Json;

namespace SourceAtlas;

internal static class UiScalePolicy
{
    public static float DefaultForRole(string role) =>
        role switch {
            "agent-tv" => 1.30F,
            "desktop-lg" => 1.17F,
            _ => 1.0F,
        };

    public static float Normalize(float requested) =>
        float.IsFinite(requested) ? Math.Clamp(requested, 1.0F, 1.70F) : 1.0F;

    public static float Step(float current, int direction) =>
        Normalize((float)Math.Round((Normalize(current) + 0.15F * Math.Sign(direction)) * 100) / 100F);

    public static float ReadPreference(string path, string role)
    {
        var fallback = DefaultForRole(role);
        try
        {
            if (!File.Exists(path)) return fallback;
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.GetProperty("schema").GetString() != "source-atlas-zoom-v1")
                return fallback;
            var stored = json.RootElement.GetProperty("zoom").GetSingle();
            return float.IsFinite(stored) && stored >= 1F && stored <= 1.70F
                ? stored : fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException
                                   or UnauthorizedAccessException or InvalidOperationException)
        {
            return fallback;
        }
    }

    public static void Store(string path, float zoom)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Missing preference path.");
        var normalized = Normalize(zoom);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Invalid settings directory.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, ".atlas-ui-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new {
                schema = "source-atlas-zoom-v1", zoom = normalized
            }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
