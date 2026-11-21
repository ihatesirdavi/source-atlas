using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;

namespace SourceAtlas;

internal sealed record SourceRow(string Kind, string PublicUrl, int Mentions, string SourceAreas, string Verification);
internal sealed record RepoRow(string Repository, int Citations, string Status, string Canonical, string License, string Starred);
internal sealed record EngagementRow(string Action, string Identity, string Status);
internal sealed record EvidenceRow(string File, string ModifiedLocal, long SizeBytes, string Sha256);

internal sealed class AuditSnapshot
{
    public string DataDirectory { get; init; } = "";
    public string AuditStatus { get; private set; } = "UNVERIFIED";
    public string ResearchStatus { get; private set; } = "NOT_CONFIGURED";
    public string LastAuditUtc { get; private set; } = "NOT_RUN";
    public int PublicLinksChecked { get; private set; }
    public int PullRequestsChecked { get; private set; }
    public int DiscoveredCount { get; private set; }
    public int VerifiedActiveGithub { get; private set; }
    public string GithubSnapshotUtc { get; private set; } = "NOT_VERIFIED";
    public int NewStars { get; private set; }
    public int NewFollows { get; private set; }
    public string RootError { get; private set; } = "";
    public List<SourceRow> Sources { get; } = [];
    public List<RepoRow> Repositories { get; } = [];
    public List<EngagementRow> Engagement { get; } = [];
    public List<EvidenceRow> Evidence { get; } = [];
    public Dictionary<string, int> SourceKinds { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static JsonElement Get(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var child) ? child : default;

    private static string Str(JsonElement obj, string key, string fallback = "") {
        var value = Get(obj, key);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static int Int(JsonElement obj, string key) {
        var value = Get(obj, key);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : 0;
    }

    private static List<JsonElement> Array(JsonElement obj, string key) {
        var value = Get(obj, key);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];
    }

    private static JsonElement LoadFile(string path, List<string> errors) {
        if (!File.Exists(path)) { errors.Add(Path.GetFileName(path) + ": NOT_FOUND"); return default; }
        try {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.Clone();
        } catch (IOException) {
            errors.Add(Path.GetFileName(path) + ": READ_FAILED");
            return default;
        } catch (JsonException) {
            errors.Add(Path.GetFileName(path) + ": INVALID_JSON");
            return default;
        }
    }

    public static AuditSnapshot Load(string directory) {
        var snap = new AuditSnapshot { DataDirectory = directory };
        var errors = new List<string>();
        var audit = LoadFile(Path.Combine(directory, "latest.json"), errors);
        snap.AuditStatus = Str(audit, "status", "UNVERIFIED");
        snap.LastAuditUtc = Str(audit, "checked_at_utc", "NOT_RUN");
        snap.PublicLinksChecked = Int(audit, "repository_count");
        snap.PullRequestsChecked = Array(audit, "pull_requests").Count;
        var research = Get(audit, "research_sources");
        snap.ResearchStatus = Str(research, "status", "NOT_CONFIGURED");

        var sources = LoadFile(Path.Combine(directory, "source-review-latest.json"), errors);
        foreach (var row in Array(sources, "candidates")) {
            var url = Str(row, "public_url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                continue;
            var areas = Array(row, "source_areas")
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList();
            snap.Sources.Add(new SourceRow(Str(row, "kind"), url, Int(row, "mentions"),
                string.Join(", ", areas), Str(row, "verification", "UNVERIFIED")));
        }
        snap.DiscoveredCount = snap.Sources.Count;
        foreach (var group in snap.Sources.GroupBy(x => x.Kind)) snap.SourceKinds[group.Key] = group.Count();

        var github = LoadFile(Path.Combine(directory, "PUBLIC_RESEARCH_GITHUB_VALIDATION_20261120.json"), errors);
        snap.GithubSnapshotUtc = Str(github, "verified_at_utc", "NOT_VERIFIED");
        foreach (var row in Array(github, "repositories")) {
            var rawUrl = Str(row, "source_url");
            var name = rawUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? rawUrl["https://github.com/".Length..] : rawUrl;
            var status = Str(row, "status", "UNVERIFIED");
            var star = Get(row, "viewer_starred");
            var starred = star.ValueKind == JsonValueKind.True ? "Yes" :
                          star.ValueKind == JsonValueKind.False ? "No" : "Unknown";
            snap.Repositories.Add(new RepoRow(name, Int(row, "citations"), status,
                Str(row, "canonical", "NOT_VERIFIED"), Str(row, "license_spdx", "Unspecified"), starred));
            if (status == "PUBLIC_ACTIVE") snap.VerifiedActiveGithub++;
        }

        var engagement = LoadFile(Path.Combine(directory, "STACK_ENGAGEMENT_RESEARCH_U006_20261120.json"), errors);
        snap.NewStars = Int(engagement, "new_stars");
        snap.NewFollows = Int(engagement, "new_follows");
        foreach (var row in Array(engagement, "actions")) {
            snap.Engagement.Add(new EngagementRow(Str(row, "kind"), Str(row, "identity"), Str(row, "status")));
        }

        foreach (var name in new[] {
            "latest.json", "source-review-latest.json",
            "PUBLIC_RESEARCH_GITHUB_VALIDATION_20261120.json",
            "STACK_ENGAGEMENT_RESEARCH_U006_20261120.json",
            "public-research-sources-config.json",
        }) {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try {
                var bytes = File.ReadAllBytes(path);
                var info = new FileInfo(path);
                snap.Evidence.Add(new EvidenceRow(name,
                    info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    info.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            } catch (IOException) { errors.Add(name + ": HASH_UNVERIFIED"); }
        }
        snap.RootError = string.Join("; ", errors);
        return snap;
    }
}
