namespace SourceAtlas;

/// <summary>Bounded in-process refresh admission, not an external scheduler.</summary>
internal sealed class LiveRunPolicy
{
    private readonly TimeSpan _period;
    private readonly TimeSpan _quiet;
    private readonly object _gate = new();
    private DateTimeOffset? _lastStarted;

    public LiveRunPolicy(TimeSpan period, TimeSpan quiet)
    {
        if (period < TimeSpan.FromMinutes(1) || quiet < TimeSpan.Zero || quiet >= period)
            throw new ArgumentOutOfRangeException(nameof(period));
        _period = period;
        _quiet = quiet;
    }

    public bool TryClaim(DateTimeOffset now, bool manual)
    {
        lock (_gate)
        {
            if (!manual && _lastStarted is { } previous && now - previous < _quiet)
                return false;
            _lastStarted = now;
            return true;
        }
    }

    public TimeSpan TimeUntilAllowed(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastStarted is not { } previous) return TimeSpan.Zero;
            var remain = _quiet - (now - previous);
            return remain > TimeSpan.Zero ? remain : TimeSpan.Zero;
        }
    }
    public bool IsPeriodicDue(DateTimeOffset now)
    {
        lock (_gate) return _lastStarted is null || now - _lastStarted >= _period;
    }

    public static bool ShouldWatch(string label) =>
        !string.IsNullOrWhiteSpace(label) && label.Length <= 40 &&
        char.IsLetter(label[0]) && label.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

    public static bool IsSourceFile(string path)
    {
        var file = Path.GetFileName(path);
        if (file.StartsWith(".", StringComparison.Ordinal) ||
            file.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("project.assets.json", StringComparison.OrdinalIgnoreCase))
            return false;
        var ext = Path.GetExtension(path);
        return new[] { ".md", ".rst", ".bib", ".json", ".yml", ".yaml", ".txt" }
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}