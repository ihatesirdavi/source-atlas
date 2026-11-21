using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SourceAtlas;

/// <summary>
/// One owner-session resident research monitor. Uses existing packaged audit,
/// approved source roots and output receipts. No GitHub write endpoints.
/// </summary>
internal sealed class LiveResearchMonitor : IDisposable
{
    private readonly string _output;
    private readonly string _root;
    private readonly string _script;
    private readonly string _python;
    private readonly LiveRunPolicy _policy =
        new(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(45));
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly CancellationTokenSource _stop = new();
    private System.Threading.Timer? _tick;
    private System.Threading.Timer? _debounce;
    private int _running;
    private bool _started;
    private bool _disposed;
    private bool _paused;
    private string _lastReason = "Not started";
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastAttempt;
    private string _state = "STARTING";
    private long _sourceEvents;
    private readonly object _lock = new();

    public event Action? Changed;
    public string Status { get { lock (_lock) return _state; } }
    public DateTimeOffset? LastSuccess { get { lock (_lock) return _lastSuccess; } }
    public DateTimeOffset? LastAttempt { get { lock (_lock) return _lastAttempt; } }
    public bool Paused { get { lock (_lock) return _paused; } }
    public int WatchedRoots => Math.Max(0, _watchers.Count - 1);
    public long SourceEvents => Interlocked.Read(ref _sourceEvents);
    public string LastReason { get { lock (_lock) return _lastReason; } }

    public LiveResearchMonitor(string output, string root)
    {
        _output = output;
        _root = root;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _script = Path.Combine(appData, "Programs", "GitHubPublishingAudit",
                               "scripts", "local_publishing_audit.py");
        _python = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                               "AppData", "Local", "Programs", "Python", "Python311", "python.exe");
    }

    public void Start()
    {
        if (_started) throw new InvalidOperationException("Live monitor already started.");
        _started = true;
        if (!Directory.Exists(_output) || !Directory.Exists(_root) || !File.Exists(_python))
        {
            SetStatus("UNVERIFIED_INSTALL", "Missing approved output, repo or Python.");
            return;
        }
        if (!VerifyInstalledAudit())
        {
            SetStatus("PACKAGE_DRIFT", "Installed source does not match its manifest.");
            return;
        }
        try
        {
            WatchOutput();
            WatchApprovedRoots();
            _debounce = new System.Threading.Timer(_ => RequestRun("Local research change"), null,
                         Timeout.Infinite, Timeout.Infinite);
            _tick = new System.Threading.Timer(_ => Tick(), null, 15000, 15000);
            SetStatus("WATCHING", "Resident live monitor started.");
            RequestRun("Startup verification");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            SetStatus("UNVERIFIED_WATCHER", e.GetType().Name);
        }
    }

    public void Pause()
    {
        lock (_lock) _paused = true;
        SetStatus("PAUSED", "Owner paused monitoring.");
    }

    public void Resume()
    {
        lock (_lock) _paused = false;
        SetStatus("WATCHING", "Owner resumed monitoring.");
        RequestRun("Resume verification", force: true);
    }

    public void RequestRun(string reason, bool force = false)
    {
        if (_disposed || !_started || Paused) return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        if (!_policy.TryClaim(DateTimeOffset.UtcNow, force))
        {
            Interlocked.Exchange(ref _running, 0);
            var wait = _policy.TimeUntilAllowed(DateTimeOffset.UtcNow) + TimeSpan.FromSeconds(1);
            try { _debounce?.Change(wait, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
            return;
        }
        _ = Task.Run(async () =>
        {
            var begin = DateTimeOffset.UtcNow;
            lock (_lock) _lastAttempt = begin;
            SetStatus("CHECKING", reason);
            var outcome = "UNVERIFIED";
            try
            {
                var info = new ProcessStartInfo {
                    FileName = _python,
                    WorkingDirectory = _root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                foreach (var arg in new[] { _script, "--root", _root,
                                            "--output-dir", _output })
                    info.ArgumentList.Add(arg);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Worker unavailable");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }
                if (process.ExitCode != 0)
                    outcome = "AUDIT_FAILED";
                else
                {
                    using var result = JsonDocument.Parse(
                        File.ReadAllBytes(Path.Combine(_output, "latest.json")));
                    var doc = result.RootElement;
                    var audited = DateTimeOffset.Parse(
                        doc.GetProperty("checked_at_utc").GetString() ?? "");
                    var sourceStatus = doc.GetProperty("research_sources")
                        .GetProperty("status").GetString();
                    if (audited < begin.AddSeconds(-3))
                        outcome = "STALE_RECEIPT";
                    else if (doc.GetProperty("status").GetString() == "PASS" &&
                             sourceStatus == "DISCOVERED_NOT_LIVE_VERIFIED")
                    {
                        outcome = "WATCHING";
                        lock (_lock) _lastSuccess = DateTimeOffset.UtcNow;
                    }
                    else outcome = "AUDIT_UNVERIFIED";
                }
            }
            catch (OperationCanceledException)
            {
                outcome = _stop.IsCancellationRequested ? "STOPPED" : "AUDIT_TIMEOUT";
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception
                                       or FormatException)
            {
                outcome = "AUDIT_UNVERIFIED_" + ex.GetType().Name;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                if (!_disposed && !Paused) SetStatus(outcome, reason);
            }
        }, _stop.Token);
    }

    private bool VerifyInstalledAudit()
    {
        try
        {
            var manifestPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_script)!,
                                                              "..", "manifest.json"));
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            if (json.RootElement.GetProperty("schema").GetString() !=
                "local-publishing-audit-installed-u007-v1") return false;
            var files = json.RootElement.GetProperty("scriptFiles").EnumerateArray();
            var matched = 0;
            foreach (var entry in files)
            {
                var name = entry.GetProperty("name").GetString() ?? "";
                if (!new[] { "local_publishing_audit.py", "research_public_sources.py",
                             "validate_profile.py", "validate_repo_references.py" }.Contains(name))
                    return false;
                var target = Path.Combine(Path.GetDirectoryName(_script)!, name);
                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
                if (!actual.Equals(entry.GetProperty("sha256").GetString(),
                                  StringComparison.OrdinalIgnoreCase)) return false;
                matched++;
            }
            return matched == 4;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                  or ArgumentException) { return false; }
    }

    private void WatchOutput()
    {
        var watcher = new FileSystemWatcher(_output, "*.json") {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        watcher.Changed += (_, e) => {
            if (Path.GetFileName(e.FullPath) is "latest.json" or "source-review-latest.json")
                RaiseChanged();
        };
        watcher.Created += (_, e) => {
            if (Path.GetFileName(e.FullPath) is "latest.json" or "source-review-latest.json")
                RaiseChanged();
        };
        watcher.Renamed += (_, e) => {
            if (Path.GetFileName(e.FullPath) is "latest.json" or "source-review-latest.json")
                RaiseChanged();
        };
        watcher.Error += (_, _) => SetStatus("UNVERIFIED_WATCHER", "Receipt watcher error");
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void WatchApprovedRoots()
    {
        var config = Path.Combine(_output, "public-research-sources-config.json");
        using var data = JsonDocument.Parse(File.ReadAllBytes(config));
        var root = data.RootElement;
        if (root.GetProperty("schema").GetString() != "public-research-sources-config-v1")
            throw new JsonException("Unrecognized research source config.");
        var entries = root.GetProperty("roots").EnumerateArray().ToArray();
        if (entries.Length is < 1 or > 16) throw new ArgumentException("Unsafe root count");
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var label = entry.GetProperty("label").GetString() ?? "";
            var path = entry.GetProperty("path").GetString() ?? "";
            if (!LiveRunPolicy.ShouldWatch(label) || !labels.Add(label) ||
                !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
                throw new ArgumentException("Unqualified source root");
            var watcher = new FileSystemWatcher(path) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => SourceChanged(e.FullPath);
            watcher.Created += (_, e) => SourceChanged(e.FullPath);
            watcher.Deleted += (_, e) => SourceChanged(e.FullPath);
            watcher.Renamed += (_, e) => SourceChanged(e.FullPath);
            watcher.Error += (_, _) => SetStatus("UNVERIFIED_WATCHER", "Research watcher error");
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void SourceChanged(string path)
    {
        if (_disposed || Paused || !LiveRunPolicy.IsSourceFile(path)) return;
        Interlocked.Increment(ref _sourceEvents);
        try { _debounce?.Change(3000, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private void Tick()
    {
        if (_disposed) return;
        if (!Paused && _policy.IsPeriodicDue(DateTimeOffset.UtcNow))
            RequestRun("Periodic public GitHub check");
        SaveState();
    }

    private void SetStatus(string status, string reason)
    {
        lock (_lock) { _state = status; _lastReason = reason; }
        SaveState();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); } catch (InvalidOperationException) { }
    }

    private void SaveState()
    {
        if (_disposed) return;
        try
        {
            var record = new {
                schema = "source-atlas-live-state-v1",
                updated_at_utc = DateTimeOffset.UtcNow.ToString("O"),
                process_id = Environment.ProcessId,
                mode = "RESIDENT_SESSION",
                status = Status,
                paused = Paused,
                last_reason = LastReason,
                last_attempt_utc = LastAttempt?.ToString("O"),
                last_success_utc = LastSuccess?.ToString("O"),
                source_events = SourceEvents,
                watched_roots = WatchedRoots,
                allowed_operations = "read-only public GitHub/approved local research",
                github_writes = false,
            };
            var temp = Path.Combine(_output, ".source-atlas-state-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(record));
            File.Move(temp, Path.Combine(_output, "source-atlas-live-state.json"), true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _tick?.Dispose();
        _debounce?.Dispose();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        _stop.Dispose();
    }
}
