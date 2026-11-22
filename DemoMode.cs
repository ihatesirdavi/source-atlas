using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;

namespace SourceAtlas;

internal static class DemoMode
{
    private static string DataDir =>
        Path.Combine(AppContext.BaseDirectory, "sample-data");

    public static bool VerifyBundledData(out string reason)
    {
        reason = "UNVERIFIED";
        foreach (var file in new[] {
            "latest.json", "source-review-latest.json",
            "PUBLIC_RESEARCH_GITHUB_VALIDATION_20261120.json",
            "STACK_ENGAGEMENT_RESEARCH_U006_20261120.json",
        })
        {
            var path = Path.Combine(DataDir, file);
            if (!File.Exists(path)) { reason = "Missing sample fixture " + file; return false; }
        }
        try
        {
            var sample = AuditSnapshot.Load(DataDir);
            if (sample.RootError.Length > 0 || sample.AuditStatus != "SAMPLE_ONLY" ||
                sample.ResearchStatus != "SAMPLE_NOT_VERIFIED" ||
                sample.VerifiedActiveGithub != 0 || sample.NewStars != 0 ||
                sample.NewFollows != 0 || sample.PullRequestsChecked != 0 ||
                sample.Sources.Count is < 1 or > 20)
            {
                reason = "Fixture claims real verification or is incomplete.";
                return false;
            }
            if (sample.Repositories.Any(x => x.Status != "SAMPLE_NOT_VERIFIED") ||
                sample.Sources.Any(x => x.Verification != "SAMPLE_NOT_VERIFIED") ||
                sample.GithubSnapshotUtc != "SAMPLE_ONLY")
            {
                reason = "Fixture includes non-demo source status.";
                return false;
            }
            reason = "SAMPLE_DATA_VERIFIED";
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            reason = ex.GetType().Name;
            return false;
        }
    }

    public static int Run(IReadOnlyDictionary<string, string> args)
    {
        if (!VerifyBundledData(out _)) return 3;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var captureForTv = args.ContainsKey("--demo-qa-dir");
        using var form = new DashboardForm(DataDir) {
            Text = "Source Atlas | OFFLINE SAMPLE (no GitHub account connected)",
            StartPosition = FormStartPosition.CenterScreen,
            Size = captureForTv ? new Size(1904, 1056) : new Size(1320, 850),
        };
        var zoom = 1.2F;
        if (args.TryGetValue("--ui-scale", out var requested) &&
            float.TryParse(requested, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            zoom = UiScalePolicy.Normalize(parsed);
        form.UseDpiStableFonts();
        form.ApplyTextScale(zoom);
        form.Shown += (_, _) => form.RelayoutForDisplay();

        if (args.TryGetValue("--demo-qa-dir", out var capture))
        {
            if (Directory.Exists(capture)) return 4;
            Directory.CreateDirectory(capture);
            form.Show();
            Application.DoEvents();
            var frames = new List<object>();
            foreach (var (page, index) in new[] {
                ("overview", 0), ("sources", 1), ("github", 2),
                ("engagement", 3), ("evidence", 4)
            })
            {
                form.SelectPage(index);
                Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                var file = Path.Combine(capture, "offline-demo-" + page + ".png");
                image.Save(file, ImageFormat.Png);
                frames.Add(new {
                    page, mode = "SAMPLE_ONLY", zoom, size = form.Size.ToString(),
                    imageBytes = new FileInfo(file).Length
                });
            }
            File.WriteAllText(Path.Combine(capture, "qa-layout.json"),
                JsonSerializer.Serialize(frames, new JsonSerializerOptions { WriteIndented = true }));
            form.Close();
            return 0;
        }
        Application.Run(form);
        return 0;
    }
}
