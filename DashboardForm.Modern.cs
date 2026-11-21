using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SourceAtlas;

internal readonly record struct WorkspaceDiagnostics(
    bool Modern, int NavigationButtons, bool NavSeparatesPages,
    bool HidesLegacyTabs, bool ReadableText, bool MetricLabelsFit,
    bool OverviewStructured, int SelectedPage, int VisiblePages, bool EvidenceTable, bool ZoomControlsAccessible);

internal sealed partial class DashboardForm
{
    private bool _modernLayout;
    private int _modernSelectedPage;
    private DataGridView _modernEvidenceGrid = null!;
    private TextBox _modernEvidenceSearch = null!;
    private Button _modernZoomMinus = null!;
    private Button _modernZoomPlus = null!;
    private TableLayoutPanel _modernRoot = null!;
    private TableLayoutPanel _modernMain = null!;
    private Panel _modernPageHost = null!;
    private readonly List<Panel> _modernPageSlots = [];
    private TableLayoutPanel _modernRail = null!;
    private FlowLayoutPanel _modernMetricStrip = null!;
    private Panel _modernHeader = null!;
    private Label _modernPageTitle = null!;
    private Label _modernPageDescription = null!;
    private Label _modernZoom = null!;
    private Label _modernFooter = null!;
    private Label _modernMonitoring = null!;
    private Label _modernMonitoringDetail = null!;
    private Label _modernAudit = null!;
    private Label _modernAuditDetail = null!;
    private Label _modernResearch = null!;
    private Label _modernResearchDetail = null!;
    private Label _modernHistory = null!;
    private Label _modernHistoryDetail = null!;
    private readonly List<Button> _modernNavigation = [];
    private readonly List<Panel> _modernMetricCards = [];
    private readonly List<Panel> _modernOverviewCards = [];
    private readonly Dictionary<Control, float> _modernBasePixels = [];
    private static readonly Color ModernRail = Color.FromArgb(11, 18, 34);
    private static readonly Color ModernPanel = Color.FromArgb(27, 40, 63);
    private static readonly Color ModernOutline = Color.FromArgb(53, 73, 100);
    private static readonly Color ModernSubtle = Color.FromArgb(172, 190, 210);

    private static Font ModernFont(float pixels, bool bold = false) =>
        new("Segoe UI", pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static Label ModernLabel(string text, float px, bool bold, Color color) =>
        new()
        {
            Text = text, Font = ModernFont(px, bold), ForeColor = color,
            BackColor = Color.Transparent, AutoSize = false,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, Margin = new Padding(0)
        };

    private static Panel ModernCard() =>
        new() { BackColor = ModernPanel, Dock = DockStyle.Fill, Margin = new Padding(6), Padding = new Padding(16) };

    public WorkspaceDiagnostics GetWorkspaceDiagnostics()
    {
        return new WorkspaceDiagnostics(
            _modernLayout,
            _modernNavigation.Count,
            _modernNavigation.Select((b, i) => b.Tag is int index && index == i).All(x => x),
            _tabs.Parent is null && _modernPageSlots.Count == 5,
            _modernPageTitle.Font.Size >= 34 && _sources.Font.Size >= 20,
            _metricCaptions.Count == 5 && _metricCaptions.All(x => x.AutoEllipsis &&
                    x.Font.Unit == GraphicsUnit.Pixel),
            _modernOverviewCards.Count == 4, _modernSelectedPage,
            _modernPageSlots.Count(x => x.Tag is true),
            _modernEvidenceGrid.Columns.Count == 4 && !_evidence.Visible,
            _modernZoomMinus.Text == "−" && _modernZoomPlus.Text == "+");
    }

    private static Button ModernNavButton(string text, int index)
    {
        var b = new Button
        {
            Text = text, Tag = index, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            Font = ModernFont(19, true), ForeColor = ModernSubtle,
            BackColor = ModernRail, TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand, Margin = new Padding(8, 3, 8, 3),
            Padding = new Padding(16, 0, 0, 0),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void BuildModernInterface()
    {
        _modernLayout = true;
        MinimumSize = new Size(1020, 680);
        BackColor = Base;
        _modernRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Base, Padding = new Padding(0),
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0)
        };
        _modernRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        _modernRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _modernRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_modernRoot);

        _modernRail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = ModernRail,
            Padding = new Padding(16), ColumnCount = 1, RowCount = 10,
            Margin = new Padding(0)
        };
        _modernRail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _modernRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        _modernRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        for (var i = 0; i < 5; i++)
            _modernRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        _modernRail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _modernRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _modernRail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _modernRoot.Controls.Add(_modernRail, 0, 0);

        var identity = new TableLayoutPanel
        {
            BackColor = ModernRail, Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(6, 4, 6, 2)
        };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        identity.Controls.Add(ModernLabel("ATLAS", 36, true, Accent), 0, 0);
        identity.Controls.Add(ModernLabel("Research console", 18, false, ModernSubtle), 0, 1);
        _modernRail.Controls.Add(identity, 0, 0);
        _modernRail.Controls.Add(ModernLabel("WORKSPACES", 16, true, ModernSubtle), 0, 1);

        var names = new[] { "Overview", "Public sources", "GitHub research", "Engagement", "Evidence" };
        for (var i = 0; i < names.Length; i++)
        {
            var b = ModernNavButton((i + 1).ToString("00", CultureInfo.InvariantCulture) + "   " + names[i], i);
            var target = i;
            b.Click += (_, _) => ModernSelectPage(target);
            _modernNavigation.Add(b);
            _modernRail.Controls.Add(b, 0, i + 2);
        }

        var zoomBar = new TableLayoutPanel
        {
            ColumnCount = 3, RowCount = 1, Dock = DockStyle.Fill,
            BackColor = ModernRail, Margin = new Padding(4)
        };
        zoomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        zoomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        zoomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        _modernZoom = ModernLabel("130%", 19, true, TextMain);
        zoomBar.Controls.Add(_modernZoom, 0, 0);
        var minus = ModernNavButton("A−", -1);
        minus.Text = "−";
        _modernZoomMinus = minus;
        minus.TextAlign = ContentAlignment.MiddleCenter;
        minus.Padding = new Padding(0);
        minus.AccessibleName = "Decrease text size";
        minus.Click += (_, _) => ChangeZoom(-1);
        var plus = ModernNavButton("A+", -1);
        plus.Text = "+";
        _modernZoomPlus = plus;
        plus.TextAlign = ContentAlignment.MiddleCenter;
        plus.Padding = new Padding(0);
        plus.AccessibleName = "Increase text size";
        plus.Click += (_, _) => ChangeZoom(1);
        zoomBar.Controls.Add(minus, 1, 0);
        zoomBar.Controls.Add(plus, 2, 0);
        _modernRail.Controls.Add(zoomBar, 0, 8);
        _modernFooter = ModernLabel("LOCAL  /  READ ONLY", 14, true, ModernSubtle);
        _modernRail.Controls.Add(_modernFooter, 0, 9);
        _footer = MakeLabel("", 9F);
        _footer.Visible = false;
        _modernRail.Controls.Add(_footer, 0, 7);

        _modernMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Base, Padding = new Padding(18, 6, 18, 14),
            Margin = new Padding(0)
        };
        _modernMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _modernMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        _modernMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 147));
        _modernMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _modernRoot.Controls.Add(_modernMain, 1, 0);

        _modernHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2), BackColor = Base };
        _modernPageTitle = ModernLabel("Research overview", 34, true, TextMain);
        _modernPageDescription = ModernLabel("Local activity, verification and source provenance", 19, false, ModernSubtle);
        _status = ModernLabel("LOCAL  /  READ ONLY", 19, true, Accent);
        _status.TextAlign = ContentAlignment.MiddleRight;
        _modernHeader.Controls.AddRange([_modernPageTitle, _modernPageDescription, _status]);
        _modernPageTitle.Dock = DockStyle.None;
        _modernPageDescription.Dock = DockStyle.None;
        _status.Dock = DockStyle.None;
        _modernHeader.Resize += (_, _) => LayoutModernHeader();
        _modernMain.Controls.Add(_modernHeader, 0, 0);

        _modernMetricStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = false, BackColor = Base,
            Margin = new Padding(0, 1, 0, 6)
        };
        foreach (var caption in new[] { "RESEARCH LINKS", "GITHUB SOURCES", "ACTIVE (SAVED)", "SAVED STARS", "SAVED FOLLOWS" })
        {
            var tile = ModernCard();
            tile.Dock = DockStyle.None;
            tile.Padding = new Padding(18, 12, 12, 6);
            tile.Margin = new Padding(4, 2, 6, 2);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
            var value = ModernLabel("-", 38, true, TextMain);
            var legend = ModernLabel(caption, 17, true, ModernSubtle);
            _metricValues.Add(value);
            _metricCaptions.Add(legend);
            layout.Controls.Add(value, 0, 0);
            layout.Controls.Add(legend, 0, 1);
            tile.Controls.Add(layout);
            _modernMetricCards.Add(tile);
            _modernMetricStrip.Controls.Add(tile);
        }
        _modernMetricStrip.Resize += (_, _) => LayoutModernMetrics();
        _modernMain.Controls.Add(_modernMetricStrip, 0, 1);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill, Margin = new Padding(0),
            Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(1, 1),
            Padding = new Point(0, 0)
        };
        var overview = ModernTabPage("Overview");
        var sources = ModernTabPage("Public Sources");
        var github = ModernTabPage("GitHub");
        var engagement = ModernTabPage("Engagement");
        var evidence = ModernTabPage("Evidence");
        _tabs.TabPages.AddRange([overview, sources, github, engagement, evidence]);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex != _modernSelectedPage)
                ModernSelectPage(_tabs.SelectedIndex);
        };
        _modernMain.Controls.Add(_tabs, 0, 2);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0), BackColor = Base
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        overview.Controls.Add(cards);
        cards.Controls.Add(BuildModernSummary("MONITOR HEALTH", out _modernMonitoring, out _modernMonitoringDetail), 0, 0);
        cards.Controls.Add(BuildModernSummary("AUDIT STATUS", out _modernAudit, out _modernAuditDetail), 1, 0);
        cards.Controls.Add(BuildModernSummary("RESEARCH INDEX", out _modernResearch, out _modernResearchDetail), 0, 1);
        cards.Controls.Add(BuildModernSummary("ENGAGEMENT HISTORY", out _modernHistory, out _modernHistoryDetail), 1, 1);
        _overview = MakeTextViewer(12);
        _overview.Visible = false;
        _overview.Size = new Size(1, 1);
        overview.Controls.Add(_overview);

        var sourcePane = MakeGridPane(sources, "Search public references", true);
        _sources = sourcePane.Grid;
        _sourcesSearch = sourcePane.Search;
        _kindFilter = sourcePane.Filter;
        AddColumn(_sources, "Kind", "Kind", 170);
        AddColumn(_sources, "Public URL", "PublicUrl", 535, true);
        AddColumn(_sources, "Refs", "Mentions", 100);
        AddColumn(_sources, "Research areas", "SourceAreas", 240);
        _sourcesSearch.TextChanged += (_, _) => FilterSources();
        _kindFilter.SelectedIndexChanged += (_, _) => FilterSources();
        _sources.CellDoubleClick += (_, _) => OpenSelectedSource();

        var repoPane = MakeGridPane(github, "Search upstream repositories", true);
        _repos = repoPane.Grid;
        _githubSearch = repoPane.Search;
        _repoFilter = repoPane.Filter;
        AddColumn(_repos, "Repository", "Repository", 320, true);
        AddColumn(_repos, "Research citations", "Citations", 135);
        AddColumn(_repos, "Status", "Status", 185);
        AddColumn(_repos, "Canonical name", "Canonical", 290);
        AddColumn(_repos, "Star", "Starred", 72);
        AddColumn(_repos, "License", "License", 125);
        _githubSearch.TextChanged += (_, _) => FilterRepos();
        _repoFilter.SelectedIndexChanged += (_, _) => FilterRepos();
        _repos.CellDoubleClick += (_, _) => OpenSelectedRepo();

        var historyPane = MakeGridPane(engagement, "Historical GitHub actions in the saved receipt", false);
        _actions = historyPane.Grid;
        AddColumn(_actions, "Action", "Action", 140);
        AddColumn(_actions, "Upstream / creator", "Identity", 510, true);
        AddColumn(_actions, "Verification", "Status", 300);

        _evidence = MakeTextViewer(12);
        _evidence.Dock = DockStyle.None;
        _evidence.Visible = false;
        evidence.Controls.Add(_evidence);
        var hashes = MakeGridPane(evidence, "Filter local evidence hashes", false);
        _modernEvidenceGrid = hashes.Grid;
        _modernEvidenceSearch = hashes.Search;
        AddColumn(_modernEvidenceGrid, "Evidence file", "File", 380);
        AddColumn(_modernEvidenceGrid, "Modified (local)", "ModifiedLocal", 205);
        AddColumn(_modernEvidenceGrid, "Bytes", "SizeBytes", 130);
        AddColumn(_modernEvidenceGrid, "SHA-256", "Sha256", 720, true);
        _modernEvidenceSearch.TextChanged += (_, _) => FilterModernEvidence();
        _modernEvidenceGrid.CellDoubleClick += (_, _) =>
        {
            if (_modernEvidenceGrid.CurrentRow?.DataBoundItem is EvidenceRow item)
                Clipboard.SetText(item.Sha256);
        };
        foreach (var grid in new[] { _sources, _repos, _actions, _modernEvidenceGrid })
        {
            grid.Font = ModernFont(20);
            grid.ColumnHeadersDefaultCellStyle.Font = ModernFont(17, true);
            grid.RowTemplate.Height = 56;
            grid.ColumnHeadersHeight = 62;
            grid.BackgroundColor = ModernPanel;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 61, 85);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 61, 85);
            grid.ShowCellToolTips = true;
        }
        _overview.Font = ModernFont(22);
        _evidence.Font = ModernFont(21);
        foreach (var pane in new[] { sourcePane, repoPane })
        {
            pane.Search.Font = ModernFont(19);
            pane.Filter.Font = ModernFont(19);
        }
        _tabs.Font = ModernFont(19);
        _modernPageHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Base };
        foreach (TabPage page in _tabs.TabPages)
        {
            var slot = new Panel { Dock = DockStyle.Fill, BackColor = Base, Visible = false };
            foreach (Control child in page.Controls.Cast<Control>().ToArray())
            {
                page.Controls.Remove(child);
                slot.Controls.Add(child);
            }
            _modernPageSlots.Add(slot);
            _modernPageHost.Controls.Add(slot);
        }
        _modernMain.Controls.Remove(_tabs);
        _modernMain.Controls.Add(_modernPageHost, 0, 2);
        _tabs.SelectedIndex = 0;
        ModernSelectPage(0);
    }
    private static TabPage ModernTabPage(string title) =>
        new(title) { BackColor = Base, ForeColor = TextMain, Padding = new Padding(0), UseVisualStyleBackColor = false };

    private Panel BuildModernSummary(string caption, out Label value, out Label detail)
    {
        var card = ModernCard();
        _modernOverviewCards.Add(card);
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = ModernPanel,
            ColumnCount = 1, RowCount = 3, Margin = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 39));
        body.Controls.Add(ModernLabel(caption, 17, true, Accent), 0, 0);
        value = ModernLabel("NOT YET VERIFIED", 34, true, TextMain);
        detail = ModernLabel("Local read-only evidence", 20, false, ModernSubtle);
        detail.AutoEllipsis = false;
        body.Controls.Add(value, 0, 1);
        body.Controls.Add(detail, 0, 2);
        card.Controls.Add(body);
        return card;
    }

    private void LayoutModernHeader()
    {
        if (!_modernLayout || _modernHeader.IsDisposed) return;
        var w = _modernHeader.ClientSize.Width;
        var h = _modernHeader.ClientSize.Height;
        var right = Math.Min(w / 3, (int)(300 * _textScale));
        _modernPageTitle.SetBounds(14, 5, Math.Max(130, w - right - 26), Math.Min(h - 10, (int)(58 * _textScale)));
        _modernPageDescription.SetBounds(14, (int)(58 * _textScale), Math.Max(125, w - 28), Math.Max(22, h - (int)(58 * _textScale) - 2));
        _status.SetBounds(Math.Max(0, w - right - 12), 5, Math.Max(100, right), Math.Min(h - 12, (int)(57 * _textScale)));
    }

    private void LayoutModernMetrics()
    {
        if (!_modernLayout || _modernMetricStrip.IsDisposed) return;
        var usable = _modernMetricStrip.ClientSize.Width;
        var width = Math.Max(105, (usable - _modernMetricCards.Count * 10) / _modernMetricCards.Count);
        var height = Math.Max(68, _modernMetricStrip.ClientSize.Height - 6);
        foreach (var card in _modernMetricCards)
            card.Size = new Size(width, height);
    }

    private void ModernSelectPage(int index)
    {
        if (index < 0 || index >= _modernNavigation.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _modernSelectedPage = index;
        UpdateModernSelection();
    }
    private void UpdateModernSelection()
    {
        if (!_modernLayout) return;
        var selected = _modernSelectedPage;
        if (selected < 0 || selected >= _modernNavigation.Count) return;
        for (var i = 0; i < _modernPageSlots.Count; i++)
        {
            _modernPageSlots[i].Tag = i == selected;
            _modernPageSlots[i].Visible = i == selected;
            if (i == selected) _modernPageSlots[i].BringToFront();
        }
        var titles = new[] { "Research overview", "Public sources", "GitHub research", "Engagement history", "Evidence and integrity" };
        var descriptions = new[] {
            "Local activity, verification and source provenance",
            "Discovered references, not necessarily live-verified",
            "Saved upstream repository verification with dated provenance",
            "Verified historical actions; no automatic social engagement",
            "Read-only local files and SHA-256 evidence"
        };
        _modernPageTitle.Text = titles[selected];
        _modernPageDescription.Text = descriptions[selected];
        for (var i = 0; i < _modernNavigation.Count; i++)
        {
            var on = i == selected;
            _modernNavigation[i].BackColor = on ? ModernPanel : ModernRail;
            _modernNavigation[i].ForeColor = on ? TextMain : ModernSubtle;
        }
    }

    private void FilterModernEvidence()
    {
        if (_modernEvidenceGrid is null || _modernEvidenceSearch is null) return;
        var term = _modernEvidenceSearch.Text.Trim();
        _modernEvidenceGrid.DataSource = _state.Evidence
            .Where(x => term.Length == 0 ||
                (x.File + " " + x.Sha256).Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    private static string ModernTime(string raw)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture);
        return raw == "NOT_RUN" ? "Never verified" : "Not independently verified";
    }

    private void UpdateModernData()
    {
        if (!_modernLayout) return;
        FilterModernEvidence();
        var demo = _state.AuditStatus == "SAMPLE_ONLY";
        _modernMonitoring.Text = demo ? "OFFLINE SAMPLE" : _live?.Status ?? "LOCAL SNAPSHOT";
        _modernMonitoringDetail.Text = demo
            ? "Synthetic data. No watcher, account or network check."
            : _live is not null
                ? $"{_live.WatchedRoots} approved roots  ·  {_live.SourceEvents} observed changes"
                : "No live agent connected. Saved audit data only.";
        _modernAudit.Text = _state.AuditStatus;
        _modernAudit.ForeColor = _state.AuditStatus is "PASS" or "SAMPLE_ONLY" ? Accent : Color.Goldenrod;
        _modernAuditDetail.Text =
            $"{_state.PublicLinksChecked} links checked  ·  {_state.PullRequestsChecked} PRs checked\nLast audit: {ModernTime(_state.LastAuditUtc)}";
        _modernResearch.Text = _state.DiscoveredCount.ToString("N0", CultureInfo.CurrentCulture) + " SOURCES";
        _modernResearchDetail.Text =
            $"{_state.Repositories.Count:N0} GitHub records in saved dataset\nSnapshot: {ModernTime(_state.GithubSnapshotUtc)}";
        _modernHistory.Text = demo ? "NO REAL ACTIONS" : $"{_state.NewStars} STARS  /  {_state.NewFollows} FOLLOWS";
        _modernHistoryDetail.Text = demo
            ? "Sample fixtures contain no actual engagement."
            : "Historical receipt only. Nothing performed in this session.";
        _modernFooter.Text = demo ? "OFFLINE  /  SAMPLE DATA" : "LOCAL  /  READ ONLY";
        _status.Text = demo ? "SAMPLE / NO LIVE DATA" : _live is null
            ? "LOCAL / " + _state.AuditStatus
            : "LIVE / " + _live.Status;
        _status.ForeColor = _state.AuditStatus == "PASS" ? Accent : Color.Goldenrod;
    }

    private void ApplyModernTextScale(float scale)
    {
        _textScale = UiScalePolicy.Normalize(scale);
        foreach (var control in Descendants(this))
        {
            if (!_modernBasePixels.TryGetValue(control, out var baseline))
            {
                baseline = control.Font.Unit == GraphicsUnit.Pixel
                    ? control.Font.Size
                    : Math.Max(12F, control.Font.SizeInPoints * 96F / 72F);
                _modernBasePixels.Add(control, baseline);
            }
            var newSize = Math.Max(15F, baseline * _textScale);
            control.Font = new Font("Segoe UI", newSize, control.Font.Style, GraphicsUnit.Pixel);
        }
        _modernRoot.ColumnStyles[0].Width = (int)Math.Round(258 * _textScale);
        _modernMain.RowStyles[0].Height = (int)Math.Round(103 * _textScale);
        _modernMain.RowStyles[1].Height = (int)Math.Round(140 * _textScale);
        _modernRail.RowStyles[0].Height = (int)Math.Round(106 * _textScale);
        _modernRail.RowStyles[1].Height = (int)Math.Round(41 * _textScale);
        for (var i = 2; i < 7; i++)
            _modernRail.RowStyles[i].Height = (int)Math.Round(57 * _textScale);
        _modernRail.RowStyles[8].Height = (int)Math.Round(66 * _textScale);
        _modernRail.RowStyles[9].Height = (int)Math.Round(46 * _textScale);
        foreach (var grid in new[] { _sources, _repos, _actions, _modernEvidenceGrid })
        {
            grid.ColumnHeadersHeight = (int)Math.Round(60 * _textScale);
            grid.RowTemplate.Height = (int)Math.Round(52 * _textScale);
            grid.ColumnHeadersDefaultCellStyle.Font = ModernFont(17 * _textScale, true);
            foreach (DataGridViewRow row in grid.Rows)
                row.Height = grid.RowTemplate.Height;
            if (grid.Parent is TableLayoutPanel pane)
            {
                pane.RowStyles[0].Height = (int)Math.Round(63 * _textScale);
                var searchBar = pane.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
                if (searchBar is not null)
                {
                    void ResizeSearch()
                    {
                        var available = searchBar.ClientSize.Width;
                        var search = searchBar.Controls.OfType<TextBox>().FirstOrDefault();
                        var filter = searchBar.Controls.OfType<ComboBox>().FirstOrDefault();
                        if (search is null) return;
                        search.Width = Math.Max(110, Math.Min(600, filter is null ? available - 28 : (int)(available * .55)));
                        if (filter is not null)
                            filter.Width = Math.Max(90, Math.Min(360, available - search.Width - 60));
                    }
                    ResizeSearch();
                    if (searchBar.Tag is not "ModernResponsive")
                    {
                        searchBar.Tag = "ModernResponsive";
                        searchBar.Resize += (_, _) => ResizeSearch();
                    }
                }
            }
        }
        _modernZoom.Text = $"{_textScale * 100:0}%";
        _modernRoot.PerformLayout();
        _modernMain.PerformLayout();
        _modernRail.PerformLayout();
        LayoutModernHeader();
        LayoutModernMetrics();
        _tabs.PerformLayout();
    }
}
