using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;

namespace SourceAtlas;

internal static class Program
{
    internal const int ActivateMessage = 0x8042;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

    [STAThread]
    private static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options.ContainsKey("--demo"))
            return DemoMode.Run(options);
        if (options.ContainsKey("--self-test"))
            return SelfTest(options.GetValueOrDefault("--evidence"));
        if (options.ContainsKey("--check-live"))
            return ResidentStateHealth.CheckAndWrite(options.GetValueOrDefault("--data"),
                                                     options.GetValueOrDefault("--evidence"));

        var dataDirectory = options.GetValueOrDefault("--data");
        var resolver = options.GetValueOrDefault("--resolver");
        var role = options.GetValueOrDefault("--role", "agent-tv");
        if (string.IsNullOrWhiteSpace(dataDirectory) ||
            string.IsNullOrWhiteSpace(resolver) ||
            !IsRoleAllowed(role, options.ContainsKey("--qa-physical") && options.ContainsKey("--qa-dir")))
            return RecordError("Approved local receipt directory, resolver and display role required; 50-inch is for isolated QA only.");

        if (!Directory.Exists(dataDirectory))
            return RecordError("Private audit data directory unavailable.");
        if (!File.Exists(resolver))
            return RecordError("Approved physical display resolver missing.");

        Rectangle target;
        try {
            target = ResolveRoleAtStartup(() => ResolveRole(resolver, role),
                options.ContainsKey("--start-hidden"), delay => System.Threading.Thread.Sleep(delay));
        }
        catch (Exception e) { return RecordError("Display role unresolved: " + e.GetType().Name); }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var bounds = role == "main-tv"
            ? new Rectangle(target.X + 8, target.Y + 8, target.Width - 16, target.Height - 24)
            : new Rectangle(target.X + 88, target.Y + 72,
                Math.Min(target.Width - 176, 1420), Math.Min(target.Height - 144, 900));        if (bounds.Width < 760 || bounds.Height < 490 || !target.Contains(bounds))
            return RecordError("Target window does not fit the selected physical display.");

        using var form = new DashboardForm(dataDirectory);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = bounds;
        var preferencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SourceAtlas", "ui-preferences.json");
        var uiScale = UiScalePolicy.ReadPreference(preferencePath, role);
        form.ConfigureZoomPersistence(preferencePath);
        if (options.TryGetValue("--ui-scale", out var zoomOverride) &&
            float.TryParse(zoomOverride, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedZoom))
            uiScale = UiScalePolicy.Normalize(parsedZoom);
        form.ApplyTextScale(uiScale);
        form.Shown += (_, _) => form.RelayoutForDisplay();

        var captureDir = options.GetValueOrDefault("--qa-dir");
        if (!string.IsNullOrEmpty(captureDir))
        {
            Directory.CreateDirectory(captureDir);
            if (options.ContainsKey("--qa-physical")) return RunPhysicalTelevisionQa(form, target, captureDir);
            form.Show();
            Application.DoEvents();
            var testSizes = new (string Name, int Width, int Height)[] {
                ("agent-tcl40-100pct", 1320, 800),
                ("lg-100pct-proxy", 1460, 870),
                ("tcl50-200pct-logical-proxy", 960, 600)
            };
            var qa = new List<object>();
            foreach (var (name, width, height) in testSizes)
            {
                form.Size = new Size(width, height);
                Application.DoEvents();
                form.RefreshData();
                form.Update();
                var pages = name.StartsWith("agent-", StringComparison.Ordinal)
                    ? new[] { ("overview", 0), ("sources", 1), ("github", 2),
                              ("engagement", 3), ("evidence", 4) }
                    : new[] { ("sources", 1) };
                foreach (var (page, index) in pages)
                {
                    form.SelectPage(index);
                    form.Update();
                    using var bitmap = new Bitmap(width, height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    var file = Path.Combine(captureDir, name + "-" + page + ".png");
                    bitmap.Save(file, ImageFormat.Png);
                    qa.Add(new {
                        profile = name, page, size = width + "x" + height,
                        bytes = new FileInfo(file).Length,
                        formContainsClient = form.ClientSize.Width > 0 && form.ClientSize.Height > 0,
                        visualRendering = "WinForms DrawToBitmap; no physical 50-inch display window"
                    });
                }
            }
            File.WriteAllText(Path.Combine(captureDir, "qa-layout.json"),
                JsonSerializer.Serialize(qa, new JsonSerializerOptions { WriteIndented = true }));
            form.Close();
            return 0;
        }

        var sourceRoot = options.GetValueOrDefault("--root");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            return RecordError("Approved profile repository --root required for resident live mode.");
        using var instance = new Mutex(true, @"Local\SourceAtlas-GithubPublishing-Live-v1", out var first);
        if (!first)
        {
            var existing = FindWindow(null, "Source Atlas | Local Research and Publishing");
            if (existing != IntPtr.Zero &&
                PostMessage(existing, ActivateMessage, IntPtr.Zero, IntPtr.Zero))
                return 0;
            return RecordError("Already running, but activation of the existing window failed.");
        }
        form.EnableLiveMonitoring(sourceRoot);
        if (options.ContainsKey("--start-hidden"))
            form.Shown += (_, _) => form.HideToTrayAtStartup();
        Application.Run(form);
        instance.ReleaseMutex();
        return 0;
    }

    private static void AssertCaptureWindow(IntPtr expected, IntPtr foreground)
    {
        if (expected == IntPtr.Zero || foreground != expected)
            throw new InvalidOperationException("Physical QA capture denied: Source Atlas is not the foreground window.");
    }

    private static string ClassifyCaptureMode(IntPtr expected, IntPtr foreground) =>
        expected == IntPtr.Zero ? "invalid" :
        expected == foreground ? "physical.CopyFromScreen" : "window.PrintWindow.not_physical_screen";
    private static int RunPhysicalTelevisionQa(DashboardForm form, Rectangle target, string captureDir)
    {
        if (target.Width != 1920 || target.Height != 1080)
            throw new InvalidOperationException("Unexpected physical television resolution.");
        form.Show();
        form.Activate();
        Application.DoEvents();
        var qa = new List<object>();
        foreach (var (page, index) in new[] {
            ("overview", 0), ("sources", 1), ("github", 2),
            ("engagement", 3), ("evidence", 4) })
        {
            form.SelectPage(index);
            form.RefreshData();
            form.Update();
            Application.DoEvents();
            System.Threading.Thread.Sleep(180);
            var captureMode = ClassifyCaptureMode(form.Handle, GetForegroundWindow());
            if (captureMode == "invalid")
                throw new InvalidOperationException("Cannot capture a missing Source Atlas window.");
            using var screen = captureMode == "physical.CopyFromScreen"
                ? new Bitmap(target.Width, target.Height)
                : new Bitmap(form.Width, form.Height);
            using (var graphics = Graphics.FromImage(screen))
            {
                if (captureMode == "physical.CopyFromScreen")
                {
                    AssertCaptureWindow(form.Handle, GetForegroundWindow());
                    graphics.CopyFromScreen(target.Location, Point.Empty, screen.Size);
                }
                else
                {
                    var hdc = graphics.GetHdc();
                    try
                    {
                        if (!PrintWindow(form.Handle, hdc, 2))
                            throw new InvalidOperationException("Source Atlas window-specific QA capture failed.");
                    }
                    finally { graphics.ReleaseHdc(hdc); }
                }
            }
            var file = Path.Combine(captureDir, $"tcl50-qa-{page}.png");
            screen.Save(file, ImageFormat.Png);
            qa.Add(new {
                page, role = "main-tv", display = "50S421", captured = captureMode,
                displaySize = $"{target.Width}x{target.Height}",
                size = $"{screen.Width}x{screen.Height}",
                file = Path.GetFileName(file), bytes = new FileInfo(file).Length
            });
        }
        File.WriteAllText(Path.Combine(captureDir, "qa-layout.json"),
            JsonSerializer.Serialize(qa, new JsonSerializerOptions { WriteIndented = true }));
        form.Close();
        return 0;
    }
    private static bool IsRoleAllowed(string role, bool isolatedPhysicalQa) =>
        role is "agent-tv" or "desktop-lg" ||
        (role == "main-tv" && isolatedPhysicalQa);
    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal)) continue;
            if (key is "--self-test" or "--start-hidden" or "--demo" or "--qa-physical" or "--check-live") { map[key] = "true"; continue; }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                map[key] = args[++i];
        }
        return map;
    }

    /// <summary>
    /// Display PnP discovery can lag a Windows logon. Retry only hidden session startup;
    /// never pick a different display, reposition a window or create another scheduler.
    /// </summary>
    private static Rectangle ResolveRoleAtStartup(Func<Rectangle> resolve, bool startHidden,
                                                   Action<TimeSpan> wait)
    {
        var delays = new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
                             TimeSpan.FromSeconds(15) };
        for (var attempt = 0; ; attempt++)
        {
            try { return resolve(); }
            catch (InvalidOperationException) when (startHidden && attempt < delays.Length)
            {
                wait(delays[attempt]);
            }
        }
    }
    private static Rectangle ResolveRole(string script, string role)
    {
        var info = new ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
                                    "-File", script, "-Role", role })
            info.ArgumentList.Add(arg);
        using var proc = Process.Start(info) ?? throw new InvalidOperationException();
        var output = proc.StandardOutput.ReadToEnd();
        _ = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(15000) || proc.ExitCode != 0)
            throw new InvalidOperationException("Native display role query failed.");
        using var doc = JsonDocument.Parse(output);
        var v = doc.RootElement;
        var reportedRole = v.GetProperty("role").GetString();
        var friendly = v.GetProperty("friendly_name").GetString();
        if (reportedRole != role ||
            (role == "agent-tv" && friendly != "40S325") ||
            (role == "desktop-lg" && friendly != "LG ULTRAGEAR") ||
            (role == "main-tv" && friendly != "50S421"))
            throw new InvalidOperationException("Unexpected display hardware identity.");
        return new Rectangle(
            v.GetProperty("x").GetInt32(), v.GetProperty("y").GetInt32(),
            v.GetProperty("width").GetInt32(), v.GetProperty("height").GetInt32());
    }

    private static int RecordError(string message)
    {
        try {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SourceAtlas");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "last-error.txt"),
                DateTimeOffset.Now.ToString("O") + ": " + message + Environment.NewLine);
        } catch (IOException) { }
        return 2;
    }

    private static int SelfTest(string? evidence)
    {
        var cases = new List<string>();
        var temp = Path.Combine(Path.GetTempPath(), "source-atlas-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "latest.json"),
                """{"status":"PASS","checked_at_utc":"2026-11-20T15:00:00Z","repository_count":5,"pull_requests":[{},{}],"research_sources":{"status":"DISCOVERED_NOT_LIVE_VERIFIED"}}""");
            File.WriteAllText(Path.Combine(temp, "source-review-latest.json"),
                """{"candidates":[{"kind":"arxiv_paper","public_url":"https://arxiv.org/abs/2610.14162","mentions":3,"source_areas":["Research"],"verification":"DISCOVERED_NOT_LIVE_VERIFIED"},{"kind":"github_repo","public_url":"https://github.com/HazyResearch/ncm","mentions":7,"source_areas":["Research"],"verification":"DISCOVERED_NOT_LIVE_VERIFIED"},{"kind":"excluded","public_url":"http://localhost/","mentions":99}]}""");
            File.WriteAllText(Path.Combine(temp, "PUBLIC_RESEARCH_GITHUB_VALIDATION_20261120.json"),
                """{"verified_at_utc":"2026-11-20T15:00:00Z","repositories":[{"source_url":"https://github.com/HazyResearch/ncm","citations":7,"status":"PUBLIC_ACTIVE","canonical":"HazyResearch/ncm","license_spdx":"MIT","viewer_starred":true}]}""");
            File.WriteAllText(Path.Combine(temp, "STACK_ENGAGEMENT_RESEARCH_U006_20261120.json"),
                """{"new_stars":1,"new_follows":2,"actions":[{"kind":"star","identity":"HazyResearch/ncm","status":"STAR_VERIFIED"}]}""");
            var s = AuditSnapshot.Load(temp);
            if (s.AuditStatus != "PASS" || s.PublicLinksChecked != 5 || s.PullRequestsChecked != 2)
                throw new Exception("Audit metadata parsing mismatch.");
            cases.Add("audit fields and PR counts");
            if (s.DiscoveredCount != 2 || s.SourceKinds["arxiv_paper"] != 1 || s.Sources[0].Mentions != 3)
                throw new Exception("Public source filtering mismatch.");
            cases.Add("public-source classification and local URL exclusion");
            if (s.VerifiedActiveGithub != 1 || s.Repositories[0].Starred != "Yes")
                throw new Exception("GitHub validation mismatch.");
            cases.Add("GitHub metadata");
            if (s.GithubSnapshotUtc != "2026-11-20T15:00:00Z")
                throw new Exception("GitHub snapshot freshness provenance missing.");
            cases.Add("saved GitHub verification timestamp is not live status");
            if (!DemoMode.VerifyBundledData(out var demoReason))
                throw new Exception("Bundled offline demo is unsafe or incomplete: " + demoReason);
            cases.Add("public offline demo is self-contained and never claims real verification");
            using (var offline = new DashboardForm(Path.Combine(AppContext.BaseDirectory, "sample-data")))
            {
                if (offline.OverviewTextForTest.Contains("LIVE LOCAL RESEARCH AGENT", StringComparison.Ordinal))
                    throw new Exception("Offline demo falsely advertises live monitoring.");
                if (!offline.OverviewTextForTest.Contains("SAMPLE_ONLY", StringComparison.Ordinal))
                    throw new Exception("Offline demo has no unambiguous sample disclosure.");
            }
            cases.Add("public demo never misrepresents sample sources as a live agent");
            if (UiScalePolicy.DefaultForRole("agent-tv") < 1.25F ||
                UiScalePolicy.DefaultForRole("desktop-lg") < 1.10F ||
                UiScalePolicy.Normalize(-2) != 1.0F ||
                UiScalePolicy.Normalize(99) != 1.70F ||
                UiScalePolicy.Normalize(float.NaN) != 1.0F)
                throw new Exception("Native large-screen zoom bounds are invalid.");
            cases.Add("readable role defaults and safe adjustable zoom limits");
            var preference = Path.Combine(temp, "ui-preferences.json");
            UiScalePolicy.Store(preference, 1.55F);
            if (Math.Abs(UiScalePolicy.ReadPreference(preference, "agent-tv") - 1.55F) > 0.001F)
                throw new Exception("Owner text zoom did not persist correctly.");
            File.WriteAllText(preference, "{not json");
            if (UiScalePolicy.ReadPreference(preference, "desktop-lg") != 1.17F)
                throw new Exception("Broken owner preference did not fall back safely.");
            cases.Add("local font zoom preference persists and invalid files degrade safely");
            using (var visual = new DashboardForm(temp))
            {
                visual.ApplyTextScale(1.45F);
                var readable = visual.GetReadabilityMetrics();
                if (readable.OverviewPoints < 16F ||
                    readable.GridPoints < 14F ||
                    readable.MetricCaptionPoints < 11.5F ||
                    readable.RowHeight < 49 ||
                    readable.TabPoints < 14F)
                    throw new Exception("Large-display fonts or grid row heights are too small.");
                if (readable.LayoutWidth <= 0 || readable.TabHeight <= 0)
                    throw new Exception("Large-display tab layout is not measurable.");
                visual.ApplyTextScale(1.0F);
                var normal = visual.GetReadabilityMetrics();
                if (normal.OverviewPoints >= readable.OverviewPoints ||
                    normal.GridPoints >= readable.GridPoints)
                    throw new Exception("Zoom controls did not change actual font sizes.");
            }
            cases.Add("native zoom changes visible fonts grids cards and tabs");
            using (var tv = new DashboardForm(temp))
            {
                tv.Size = new Size(1900, 1040);
                tv.ApplyTextScale(1.30F);
                tv.PerformLayout();
                var diagnostic = tv.GetWorkspaceDiagnostics();
                if (!diagnostic.Modern || diagnostic.NavigationButtons != 5 ||
                    !diagnostic.NavSeparatesPages || !diagnostic.HidesLegacyTabs ||
                    !diagnostic.ReadableText || !diagnostic.MetricLabelsFit ||
                    !diagnostic.OverviewStructured)
                    throw new Exception("50-inch responsive workspace contract violated.");
            }
            cases.Add("real television workspace provides unclipped structure and readable navigation");
            using (var tvNavigation = new DashboardForm(temp))
            {
                tvNavigation.SelectPage(1);
                var sourcesPage = tvNavigation.GetWorkspaceDiagnostics();
                if (sourcesPage.SelectedPage != 1 || sourcesPage.VisiblePages != 1)
                    throw new Exception("Selecting Public Sources did not swap to the sources view.");
                tvNavigation.SelectPage(4);
                var evidencePage = tvNavigation.GetWorkspaceDiagnostics();
                if (evidencePage.SelectedPage != 4 || evidencePage.VisiblePages != 1)
                    throw new Exception("Selecting Evidence did not swap the visible page.");
            }
            cases.Add("every page selection updates one visible workspace panel");
            using (var polish = new DashboardForm(temp))
            {
                polish.ApplyTextScale(1.30F);
                var ui = polish.GetWorkspaceDiagnostics();
                if (!ui.EvidenceTable || !ui.ZoomControlsAccessible)
                    throw new Exception("Large-screen evidence and font controls are not accessible.");
            }
            cases.Add("evidence is tabular and zoom buttons remain visible on television");
            if (!IsRoleAllowed("main-tv", true) || IsRoleAllowed("main-tv", false) ||
                !IsRoleAllowed("agent-tv", false) || IsRoleAllowed("unknown", true))
                throw new Exception("50-inch physical preview must never start a second live agent.");
            cases.Add("main television is authorized for isolated visual QA only");
            // Reject physical screenshots when another application covers the expected window.
            AssertCaptureWindow(new IntPtr(51), new IntPtr(51));
            foreach (var (expected, actual) in new[] {
                (new IntPtr(51), new IntPtr(52)),
                (IntPtr.Zero, IntPtr.Zero),
                (new IntPtr(51), IntPtr.Zero) })
            {
                var rejected = false;
                try { AssertCaptureWindow(expected, actual); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected)
                    throw new Exception("Physical QA accepted another foreground window or an invalid handle.");
            }
            cases.Add("physical screen capture requires this app to be the foreground window");
            if (ClassifyCaptureMode(new IntPtr(51), new IntPtr(51)) != "physical.CopyFromScreen" ||
                ClassifyCaptureMode(new IntPtr(51), new IntPtr(52)) != "window.PrintWindow.not_physical_screen" ||
                ClassifyCaptureMode(IntPtr.Zero, IntPtr.Zero) != "invalid")
                throw new Exception("Capture mode incorrectly labels an obstructed or invalid window as physical evidence.");
            cases.Add("foreground screen and window-only screenshots have distinct evidence classes");
            if (s.NewStars != 1 || s.NewFollows != 2 || s.Engagement.Count != 1)
                throw new Exception("Engagement parsing mismatch.");
            cases.Add("bounded engagement receipts");
            if (s.Evidence.Count != 4 || s.Evidence.Any(x => x.Sha256.Length != 64))
                throw new Exception("Evidence hash mismatch.");
            cases.Add("immutable source hash metadata");
            var malformed = Path.Combine(temp, "source-review-latest.json");
            File.WriteAllText(malformed, "{invalid");
            var degraded = AuditSnapshot.Load(temp);
            if (!degraded.RootError.Contains("INVALID_JSON") || degraded.DiscoveredCount != 0)
                throw new Exception("Malformed source receipt should degrade safely.");
            cases.Add("malformed source safety");
            var policy = new LiveRunPolicy(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(45));
            var t0 = DateTimeOffset.Parse("2026-11-21T06:30:00+00:00");
            if (!policy.TryClaim(t0, false)) throw new Exception("First live run not admitted.");
            if (policy.TryClaim(t0.AddSeconds(4), false)) throw new Exception("Event storm ran twice.");
            if (!policy.TryClaim(t0.AddSeconds(46), false)) throw new Exception("Debounce over-throttled.");
            cases.Add("one process coalesces source events");
            var quiet = policy.TimeUntilAllowed(t0.AddSeconds(48));
            if (quiet != TimeSpan.FromSeconds(43))
                throw new Exception("Throttled file change cannot be deferred correctly.");
            cases.Add("deferred source change quiet-time calculation");

            if (!policy.TryClaim(t0.AddSeconds(47), true))
                throw new Exception("Manual refresh should bypass quiet period.");
            if (policy.IsPeriodicDue(t0.AddSeconds(48)))
                throw new Exception("Periodic run must not double-run after manual.");
            if (!policy.IsPeriodicDue(t0.AddMinutes(16)))
                throw new Exception("Periodic refresh did not become due.");
            cases.Add("bounded live refresh and manual override");

            if (!LiveRunPolicy.IsSourceFile("test.md") ||
                !LiveRunPolicy.IsSourceFile("paper.bib") ||
                LiveRunPolicy.IsSourceFile("my_password.txt") ||
                LiveRunPolicy.IsSourceFile("app.exe") ||
                LiveRunPolicy.IsSourceFile("project.assets.json"))
                throw new Exception("Source event filtering mismatch.");
            cases.Add("research source change allowlist");

            if (!LiveRunPolicy.ShouldWatch("ResearchDocs") ||
                LiveRunPolicy.ShouldWatch("bad/research") ||
                LiveRunPolicy.ShouldWatch("../private"))
                throw new Exception("Watcher root identity validation mismatch.");
            cases.Add("bounded source watcher labels");
            // U015 RED: a persisted WATCHING receipt alone never proves a resident survived reboot.
            var checkpoint = DateTimeOffset.Parse("2026-11-22T12:00:00+00:00");
            var freshState = JsonSerializer.Serialize(new {
                schema = "source-atlas-live-state-v1",
                updated_at_utc = checkpoint.AddMinutes(-1).ToString("O"),
                process_id = 4242,
                status = "WATCHING"
            });
            var staleState = JsonSerializer.Serialize(new {
                schema = "source-atlas-live-state-v1",
                updated_at_utc = checkpoint.AddHours(-6).ToString("O"),
                process_id = 4242,
                status = "WATCHING"
            });
            if (ResidentStateHealth.Assess(null, checkpoint, _ => null).Verdict != "STATE_MISSING" ||
                ResidentStateHealth.Assess("{broken", checkpoint, _ => null).Verdict != "STATE_INVALID")
                throw new Exception("Missing/malformed resident receipt must not report a running monitor.");
            cases.Add("resident status handles absent and invalid receipts");
            if (ResidentStateHealth.Assess(staleState, checkpoint, _ => checkpoint.AddHours(-7)).Verdict != "STATE_STALE")
                throw new Exception("Old WATCHING state was incorrectly accepted after reboot.");
            cases.Add("stale persisted WATCHING is not operational liveness");
            if (ResidentStateHealth.Assess(freshState, checkpoint, _ => null).Verdict != "PROCESS_UNVERIFIED")
                throw new Exception("Missing process cannot be trusted from a saved PID.");
            cases.Add("saved PID without verified native process is untrusted");
            if (ResidentStateHealth.Assess(freshState, checkpoint, _ => checkpoint).Verdict != "PID_REUSED")
                throw new Exception("A reused PID must not satisfy stale ownership.");
            cases.Add("process start time prevents PID-reuse false positive");
            var live = ResidentStateHealth.Assess(freshState, checkpoint, _ => checkpoint.AddMinutes(-2));
            if (live.Verdict != "PROCESS_CONFIRMED" || live.ReportedStatus != "WATCHING")
                throw new Exception("Fresh status plus matching process lifetime must be distinguishable.");
            cases.Add("only fresh state and verified process yield process-level confirmation");
            // U016 RED: PnP can be unavailable briefly during user logon.
            var discoveryTries = 0;
            var retryDelays = new List<TimeSpan>();
            var eventuallyResolved = ResolveRoleAtStartup(() => {
                discoveryTries++;
                if (discoveryTries <= 2) throw new InvalidOperationException("PnP not yet ready");
                return new Rectangle(1, 2, 1920, 1080);
            }, true, retryDelays.Add);
            if (discoveryTries != 3 || eventuallyResolved.Width != 1920 ||
                retryDelays.Count != 2 || retryDelays[0] != TimeSpan.FromSeconds(5) ||
                retryDelays[1] != TimeSpan.FromSeconds(10))
                throw new Exception("Hidden startup did not recover after bounded display discovery retries.");
            cases.Add("hidden startup retries transient resolver failure with bounded delays");

            var ordinaryAttempts = 0;
            var ordinaryFailed = false;
            try {
                ResolveRoleAtStartup(() => {
                    ordinaryAttempts++;
                    throw new InvalidOperationException("Unresolved physical display");
                }, false, _ => throw new Exception("Normal UI must not wait on role failure"));
            } catch (InvalidOperationException) { ordinaryFailed = true; }
            if (!ordinaryFailed || ordinaryAttempts != 1)
                throw new Exception("Non-hidden launch must fail immediately without hidden retries.");
            cases.Add("non-hidden display failure refuses retry or fallback display");

            var exhaustedTries = 0;
            var exhaustedWaits = new List<TimeSpan>();
            var exhaustedAsExpected = false;
            try {
                ResolveRoleAtStartup(() => {
                    exhaustedTries++;
                    throw new InvalidOperationException("TCL PnP unresolved");
                }, true, exhaustedWaits.Add);
            } catch (InvalidOperationException) { exhaustedAsExpected = true; }
            if (!exhaustedAsExpected || exhaustedTries != 4 || exhaustedWaits.Count != 3 ||
                exhaustedWaits.Aggregate(TimeSpan.Zero, (a, b) => a + b) != TimeSpan.FromSeconds(30))
                throw new Exception("Startup must fail closed after thirty seconds without guessing a display.");
            cases.Add("hidden startup stops after bounded retries with no display fallback");
            if (!string.IsNullOrEmpty(evidence))
                File.WriteAllText(evidence, JsonSerializer.Serialize(
                    new { status = "PASS", tests = cases, count = cases.Count },
                    new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception e) {
            if (!string.IsNullOrEmpty(evidence))
                File.WriteAllText(evidence, JsonSerializer.Serialize(new { status="FAIL", failure=e.Message, tests=cases }));
            return 1;
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }
}
