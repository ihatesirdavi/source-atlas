using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SourceAtlas;

/// <summary>Read-only resident process check. A persisted state is not a live heartbeat.</summary>
internal static class ResidentStateHealth
{
    internal sealed record Report(string Verdict, string? ReportedStatus, int? ProcessId,
                                  string CheckedAtUtc, string? StateUpdatedAtUtc);

    internal static Report Assess(string? raw, DateTimeOffset now,
                                  Func<int, DateTimeOffset?> verifiedProcessStart)
    {
        Report Result(string verdict, string? status = null, int? pid = null,
                      DateTimeOffset? updated = null) => new(
            verdict, status, pid, now.ToUniversalTime().ToString("O"),
            updated?.ToUniversalTime().ToString("O"));

        if (string.IsNullOrWhiteSpace(raw)) return Result("STATE_MISSING");
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) ||
                schema.GetString() != "source-atlas-live-state-v1" ||
                !root.TryGetProperty("updated_at_utc", out var updatedValue) ||
                !DateTimeOffset.TryParse(updatedValue.GetString(), out var updated) ||
                !root.TryGetProperty("process_id", out var processId) ||
                !processId.TryGetInt32(out var pid) || pid <= 0 ||
                !root.TryGetProperty("status", out var statusValue) ||
                string.IsNullOrWhiteSpace(statusValue.GetString()))
                return Result("STATE_INVALID");

            var reported = statusValue.GetString();
            if (updated > now.AddMinutes(2))
                return Result("CLOCK_INCONSISTENT", reported, pid, updated);
            if (now - updated > TimeSpan.FromMinutes(35))
                return Result("STATE_STALE", reported, pid, updated);
            var started = verifiedProcessStart(pid);
            if (started is null)
                return Result("PROCESS_UNVERIFIED", reported, pid, updated);
            if (started > updated.AddSeconds(3) || started > now)
                return Result("PID_REUSED", reported, pid, updated);
            // This confirms a process identity only. The audit can still be stale or failed.
            return Result("PROCESS_CONFIRMED", reported, pid, updated);
        }
        catch (JsonException) { return Result("STATE_INVALID"); }
        catch (InvalidOperationException) { return Result("STATE_INVALID"); }
        catch (FormatException) { return Result("STATE_INVALID"); }
    }

    private static DateTimeOffset? VerifySourceAtlasProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals("SourceAtlas", StringComparison.OrdinalIgnoreCase) ||
                process.SessionId != Process.GetCurrentProcess().SessionId)
                return null;
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
        catch (NotSupportedException) { return null; }
    }

    internal static int CheckAndWrite(string? dataDirectory, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory) || string.IsNullOrWhiteSpace(evidence) ||
            !Path.IsPathFullyQualified(dataDirectory) || !Path.IsPathFullyQualified(evidence) ||
            !Directory.Exists(dataDirectory))
            return 2;
        try
        {
            var source = Path.Combine(dataDirectory, "source-atlas-live-state.json");
            var raw = File.Exists(source) ? File.ReadAllText(source) : null;
            var result = Assess(raw, DateTimeOffset.UtcNow, VerifySourceAtlasProcess);
            var receipt = new {
                schema = "source-atlas-resident-check-v1",
                verdict = result.Verdict,
                reported_status = result.ReportedStatus,
                process_id = result.ProcessId,
                checked_at_utc = result.CheckedAtUtc,
                state_updated_at_utc = result.StateUpdatedAtUtc,
                evidence_grade = "read-only process and saved state, not audit verification",
                started_or_changed_resident = false
            };
            using var file = new FileStream(evidence, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, receipt, new JsonSerializerOptions { WriteIndented = true });
            return result.Verdict == "PROCESS_CONFIRMED" ? 0 : 3;
        }
        catch (IOException) { return 2; }
        catch (UnauthorizedAccessException) { return 2; }
        catch (ArgumentException) { return 2; }
    }
}