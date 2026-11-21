using System.Drawing;
using System.Windows.Forms;

namespace SourceAtlas;

internal readonly record struct ReadabilityMetrics(
    float OverviewPoints, float GridPoints, float MetricCaptionPoints,
    int RowHeight, float TabPoints, int LayoutWidth, int TabHeight);

internal sealed partial class DashboardForm
{
    private float _textScale = 1.0F;
    private bool _pixelFontMode;
    public void UseDpiStableFonts() => _pixelFontMode = true;
    private string? _zoomPreferencePath;
    private readonly Dictionary<Control, float> _originalFontPoints = [];
    private Button? _zoomMinus;
    private Button? _zoomPlus;
    private Label? _zoomValue;
    private bool _responsiveHandlersAdded;

    public void ConfigureZoomPersistence(string location) => _zoomPreferencePath = location;

    private void ChangeZoom(int direction)
    {
        var next = UiScalePolicy.Step(_textScale, direction);
        ApplyTextScale(next);
        if (_zoomPreferencePath is { Length: > 0 } location)
        {
            try { UiScalePolicy.Store(location, next); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    internal string OverviewTextForTest => _overview.Text;
    public ReadabilityMetrics GetReadabilityMetrics() =>
        _modernLayout
            ? new ReadabilityMetrics(_overview.Font.Size * 0.75F, _sources.Font.Size * 0.75F,
                _metricCaptions[0].Font.Size * 0.75F, _sources.RowTemplate.Height,
                _tabs.Font.Size * 0.75F, ClientSize.Width, _tabs.Height)
            : new ReadabilityMetrics(_overview.Font.SizeInPoints, _sources.Font.SizeInPoints,
                _metricCaptions[0].Font.SizeInPoints, _sources.RowTemplate.Height,
                _tabs.Font.SizeInPoints, ClientSize.Width, _tabs.Height);
    public void ApplyTextScale(float scale)
    {
        _textScale = UiScalePolicy.Normalize(scale);
        if (_modernLayout) { ApplyModernTextScale(_textScale); return; }
        var layout = Controls.OfType<TableLayoutPanel>().FirstOrDefault()
            ?? throw new InvalidOperationException("Dashboard root layout is unavailable");
        EnsureZoomControls(layout);
        foreach (var control in Descendants(layout))
        {
            if (!_originalFontPoints.TryGetValue(control, out var original))
            {
                original = control.Font.SizeInPoints;
                _originalFontPoints.Add(control, original);
            }
            var size = _pixelFontMode ? Math.Max(12F, original * _textScale * 96F / 72F) : Math.Max(8F, original * _textScale);
            control.Font = new Font(control.Font.FontFamily, size,
                control.Font.Style, _pixelFontMode ? GraphicsUnit.Pixel : GraphicsUnit.Point);
        }
        layout.RowStyles[0].Height = Pixels(74);
        layout.RowStyles[1].Height = Pixels(114);
        layout.RowStyles[3].Height = Pixels(50);

        if (layout.GetControlFromPosition(0, 0) is Panel header)
        {
            var title = header.Controls.OfType<Label>().First(x => x.Text == "SOURCE ATLAS");
            var subtitle = header.Controls.OfType<Label>().First(x =>
                x.Text.StartsWith("GitHub publishing", StringComparison.Ordinal));
            title.SetBounds(Pixels(4), Pixels(2), Pixels(350), Pixels(44));
            subtitle.SetBounds(Pixels(5), Pixels(44), Math.Min(Pixels(760), Math.Max(300, header.Width - Pixels(360))), Pixels(26));
            _status.Size = new Size(Pixels(340), Pixels(40));
            _status.Top = Pixels(5);
            _status.Left = Math.Max(0, header.ClientSize.Width - _status.Width);
        }
        if (layout.GetControlFromPosition(0, 1) is FlowLayoutPanel metrics)
        {
            if (!_responsiveHandlersAdded)
            {
                metrics.SizeChanged += (_, _) => ArrangeMetrics(metrics);
            }
            ArrangeMetrics(metrics);
        }
        foreach (var grid in new[] { _sources, _repos, _actions })
        {
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Pixels(43);
            grid.RowTemplate.Height = Pixels(40);
            grid.ColumnHeadersDefaultCellStyle.Font =
                new Font("Segoe UI", 10.4F * _textScale * (_pixelFontMode ? 96F / 72F : 1F), FontStyle.Bold, _pixelFontMode ? GraphicsUnit.Pixel : GraphicsUnit.Point);
            foreach (DataGridViewRow row in grid.Rows) row.Height = Pixels(40);
        }
        foreach (var control in new[] { _sources, _repos, _actions })
        {
            if (control.Parent is not TableLayoutPanel pane) continue;
            pane.RowStyles[0].Height = Pixels(58);
            foreach (var input in pane.Controls.OfType<FlowLayoutPanel>()
                         .SelectMany(bar => bar.Controls.Cast<Control>()))
            {
                if (input is TextBox search)
                {
                    search.Width = Pixels(330);
                    search.Height = Pixels(36);
                }
                else if (input is ComboBox filter)
                {
                    filter.Width = Pixels(235);
                    filter.Height = Pixels(36);
                }
            }
        }
        if (layout.GetControlFromPosition(0, 3) is Panel footer)
            ArrangeFooter(footer);

        if (_zoomValue is not null)
            _zoomValue.Text = $"{_textScale * 100:0}%";
        if (!_responsiveHandlersAdded)
        {
            if (layout.GetControlFromPosition(0, 3) is Panel footerResize)
                footerResize.SizeChanged += (_, _) => ArrangeFooter(footerResize);
            _responsiveHandlersAdded = true;
        }
        layout.PerformLayout();
        _tabs.PerformLayout();
    }

    public void RelayoutForDisplay()
    {
        if (_modernLayout) { LayoutModernHeader(); LayoutModernMetrics(); return; }
        var root = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (root is null) return;
        root.SuspendLayout();
        root.RowStyles[0].Height = Pixels(74);
        root.RowStyles[1].Height = Pixels(114);
        root.RowStyles[3].Height = Pixels(50);
        root.ResumeLayout(true);
        root.PerformLayout();
        if (root.GetControlFromPosition(0, 1) is FlowLayoutPanel metrics)
        {
            ArrangeMetrics(metrics);
            metrics.PerformLayout();
        }
        if (root.GetControlFromPosition(0, 3) is Panel footer)
            ArrangeFooter(footer);
        _tabs.PerformLayout();
    }
    private int Pixels(int baseline) => (int)Math.Round(baseline * _textScale);

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }

    private void ArrangeMetrics(FlowLayoutPanel metrics)
    {
        var available = Math.Max(0, metrics.ClientSize.Width);
        var width = Math.Max(125, Math.Min(300, (available - Pixels(92)) / 5));
        foreach (var card in metrics.Controls.OfType<Panel>())
        {
            card.Width = width;
            card.Height = Pixels(91);
            var labels = card.Controls.OfType<Label>().ToArray();
            if (labels.Length < 2) continue;
            labels[0].SetBounds(Pixels(14), Pixels(4),
                Math.Max(60, width - Pixels(20)), Pixels(49));
            labels[1].SetBounds(Pixels(14), Pixels(52),
                Math.Max(60, width - Pixels(20)), Pixels(28));
            labels[1].AutoEllipsis = true;
        }
    }

    private void EnsureZoomControls(TableLayoutPanel root)
    {
        if (_zoomMinus is not null) return;
        if (root.GetControlFromPosition(0, 3) is not Panel footer)
            throw new InvalidOperationException("Dashboard footer is unavailable");
        _zoomMinus = MakeButton("A−", (_, _) =>
            ChangeZoom(-1));
        _zoomPlus = MakeButton("A+", (_, _) =>
            ChangeZoom(+1));
        _zoomMinus.AccessibleName = "Decrease text size";
        _zoomPlus.AccessibleName = "Increase text size";
        _zoomValue = MakeLabel("100%", 10.0F, true, TextMain);
        _zoomValue.TextAlign = ContentAlignment.MiddleCenter;
        footer.Controls.AddRange([_zoomMinus, _zoomPlus, _zoomValue]);
    }

    private void ArrangeFooter(Panel footer)
    {
        if (_zoomMinus is null || _zoomPlus is null || _zoomValue is null) return;
        var refresh = footer.Controls.OfType<Button>().FirstOrDefault(x =>
            x != _zoomMinus && x != _zoomPlus);
        if (refresh is null) return;
        var height = Math.Max(30, footer.ClientSize.Height - Pixels(9));
        var buttonW = Pixels(43);
        var labelW = Pixels(60);
        var refreshW = Pixels(140);
        var right = Math.Max(0, footer.ClientSize.Width - 3);
        refresh.SetBounds(Math.Max(0, right - refreshW), 3, refreshW, height);
        right = refresh.Left - Pixels(9);
        _zoomPlus.SetBounds(Math.Max(0, right - buttonW), 3, buttonW, height);
        right = _zoomPlus.Left - Pixels(4);
        _zoomMinus.SetBounds(Math.Max(0, right - buttonW), 3, buttonW, height);
        right = _zoomMinus.Left - Pixels(4);
        _zoomValue.SetBounds(Math.Max(0, right - labelW), 3, labelW, height);
        _footer.Width = Math.Max(125, _zoomValue.Left - Pixels(15));
        _footer.Height = height;
        _footer.Top = 3;
    }
}
