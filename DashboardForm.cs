using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SourceAtlas;

internal sealed partial class DashboardForm : Form
{
    private static readonly Color Base = Color.FromArgb(15, 23, 42);
    private static readonly Color Surface = Color.FromArgb(24, 34, 55);
    private static readonly Color TextMain = Color.FromArgb(229, 236, 244);
    private static readonly Color TextMuted = Color.FromArgb(161, 177, 199);
    private static readonly Color Accent = Color.FromArgb(85, 186, 236);
    private readonly string _dataDirectory;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 45000 };
    private AuditSnapshot _state;
    private Label _status = null!;
    private Label _footer = null!;
    private RichTextBox _overview = null!;
    private RichTextBox _evidence = null!;
    private readonly List<Label> _metricValues = [];
    private readonly List<Label> _metricCaptions = [];
    private TabControl _tabs = null!;
    private DataGridView _sources = null!;
    private DataGridView _repos = null!;
    private DataGridView _actions = null!;
    private TextBox _sourcesSearch = null!;
    private TextBox _githubSearch = null!;
    private ComboBox _kindFilter = null!;
    private ComboBox _repoFilter = null!;

    public DashboardForm(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _state = AuditSnapshot.Load(dataDirectory);
        Text = "Source Atlas | Local Research and Publishing";
        BackColor = Base;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(760, 490);
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1280, 800);
        BuildInterface();
        RefreshData();
        _timer.Tick += (_, _) => RefreshData();
        _timer.Start();
        FormClosed += (_, _) => _timer.Stop();
    }

    private LiveResearchMonitor? _live;
    private NotifyIcon? _tray;
    private bool _exitRequested;

    public void EnableLiveMonitoring(string approvedRoot)
    {
        if (_live is not null) throw new InvalidOperationException("Monitor already attached.");
        var monitor = new LiveResearchMonitor(_dataDirectory, approvedRoot);
        _live = monitor;
        monitor.Changed += () =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    RefreshData();
                    UpdateLiveIndicator();
                }));
            }
            catch (InvalidOperationException) { }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Source Atlas", null, (_, _) => RestoreWindow());
        menu.Items.Add("Check research and GitHub now", null,
                       (_, _) => monitor.RequestRun("Manual owner refresh", force: true));
        var pause = new ToolStripMenuItem("Pause monitoring");
        pause.Click += (_, _) =>
        {
            if (monitor.Paused) monitor.Resume();
            else monitor.Pause();
            pause.Text = monitor.Paused ? "Resume monitoring" : "Pause monitoring";
        };
        menu.Items.Add(pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Source Atlas", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });
        _tray = new NotifyIcon {
            Text = "Source Atlas live research monitor",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        Shown += (_, _) => monitor.Start();
        FormClosing += (_, e) =>
        {
            if (!_exitRequested && e.CloseReason is not CloseReason.WindowsShutDown and not CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
                ShowInTaskbar = false;
                Hide();
            }
            else
            {
                monitor.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
            }
        };
        UpdateLiveIndicator();
    }

    public void HideToTrayAtStartup()
    {
        ShowInTaskbar = false;
        Hide();
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Program.ActivateMessage)
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(RestoreWindow));
            return;
        }
        base.WndProc(ref message);
    }

    private void RestoreWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshData();
    }

    private void UpdateLiveIndicator()
    {
        if (_live is null) return;
        var status = _live.Status;
        _status.Text = "LIVE  /  " + status;
        _status.ForeColor = status is "WATCHING" or "CHECKING" ? Accent : Color.Goldenrod;
        if (_tray is not null)
            _tray.Text = ("Source Atlas: " + status)[..Math.Min(63, ("Source Atlas: " + status).Length)];
    }
    private static Label MakeLabel(string text, float size = 11F, bool bold = false,
                                   Color? color = null)
    {
        return new Label {
            Text = text, ForeColor = color ?? TextMain, BackColor = Color.Transparent,
            AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular,
                            GraphicsUnit.Point),
        };
    }

    private static RichTextBox MakeTextViewer(float size)
    {
        return new RichTextBox {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = Surface, ForeColor = TextMain,
            ReadOnly = true, DetectUrls = false, TabStop = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true, Font = new Font("Segoe UI", size),
        };
    }
    private static Button MakeButton(string text, EventHandler handler)
    {
        var button = new Button {
            Text = text, BackColor = Color.FromArgb(36, 53, 76), ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Size = new Size(135, 40), Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(64, 90, 122);
        button.Click += handler;
        return button;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = Base, Padding = new Padding(18, 12, 18, 12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Base };
        var title = MakeLabel("SOURCE ATLAS", 22F, true, Accent);
        title.Bounds = new Rectangle(4, 2, 330, 38);
        var subtitle = MakeLabel("GitHub publishing  /  public research  /  local evidence", 10F, false, TextMuted);
        subtitle.Bounds = new Rectangle(5, 38, 600, 28);
        _status = MakeLabel("READ ONLY", 10F, true, Accent);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.Bounds = new Rectangle(665, 7, 300, 32);
        header.Controls.AddRange([title, subtitle, _status]);
        header.SizeChanged += (_, _) => _status.Left = Math.Max(0, header.ClientSize.Width - _status.Width - 8);
        root.Controls.Add(header, 0, 0);

        var metrics = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            BackColor = Base, WrapContents = false,
            Margin = new Padding(0, 2, 0, 8),
        };
        foreach (var caption in new[] { "RESEARCH LINKS", "GITHUB SOURCES", "PUBLIC ACTIVE",
                                       "NEW STARS", "NEW FOLLOWS" }) {
            var card = new Panel {
                BackColor = Surface, Size = new Size(178, 88),
                Margin = new Padding(4, 3, 9, 3), Padding = new Padding(10),
            };
            var value = MakeLabel("-", 21F, true, TextMain);
            value.Bounds = new Rectangle(14, 9, 152, 43);
            var legend = MakeLabel(caption, 8.3F, true, TextMuted);
            legend.Bounds = new Rectangle(14, 52, 152, 25);
            card.Controls.AddRange([value, legend]);
            _metricValues.Add(value);
            _metricCaptions.Add(legend);
            metrics.Controls.Add(card);
        }
        metrics.SizeChanged += (_, _) => {
            var cellWidth = Math.Max(120, Math.Min(178, (metrics.ClientSize.Width - 92) / 5));
            foreach (Control control in metrics.Controls) control.Width = cellWidth;
        };        root.Controls.Add(metrics, 0, 1);

        var tabs = new TabControl {
            Dock = DockStyle.Fill, Appearance = TabAppearance.Normal,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Margin = new Padding(0),
        };
        var home = MakeTab("Overview");
        var source = MakeTab("Public Sources");
        var github = MakeTab("GitHub");
        var engagement = MakeTab("Engagement");
        var evidence = MakeTab("Evidence");
        tabs.TabPages.AddRange([home, source, github, engagement, evidence]);
        _tabs = tabs;
        root.Controls.Add(tabs, 0, 2);

        _overview = MakeTextViewer(11.8F);
        _overview.Dock = DockStyle.Fill;
        _overview.Padding = new Padding(24);

        home.Controls.Add(_overview);

        var sourceParts = MakeGridPane(source, "Search public references", true);
        _sources = sourceParts.Grid;
        _sourcesSearch = sourceParts.Search;
        _kindFilter = sourceParts.Filter;
        AddColumn(_sources, "Kind", "Kind", 170);
        AddColumn(_sources, "Public URL", "PublicUrl", 535, true);
        AddColumn(_sources, "Mentions", "Mentions", 100);
        AddColumn(_sources, "Research areas", "SourceAreas", 240);
        _sourcesSearch.TextChanged += (_, _) => FilterSources();
        _kindFilter.SelectedIndexChanged += (_, _) => FilterSources();
        _sources.CellDoubleClick += (_, _) => OpenSelectedSource();

        var gitParts = MakeGridPane(github, "Search upstream repositories", true);
        _repos = gitParts.Grid;
        _githubSearch = gitParts.Search;
        _repoFilter = gitParts.Filter;
        AddColumn(_repos, "Repository", "Repository", 320, true);
        AddColumn(_repos, "Research citations", "Citations", 135);
        AddColumn(_repos, "Status", "Status", 185);
        AddColumn(_repos, "Canonical name", "Canonical", 290);
        AddColumn(_repos, "Star", "Starred", 72);
        AddColumn(_repos, "License", "License", 125);
        _githubSearch.TextChanged += (_, _) => FilterRepos();
        _repoFilter.SelectedIndexChanged += (_, _) => FilterRepos();
        _repos.CellDoubleClick += (_, _) => OpenSelectedRepo();

        var engagementPane = MakeGridPane(engagement, "Verified one-time GitHub actions", false);
        _actions = engagementPane.Grid;
        AddColumn(_actions, "Action", "Action", 110);
        AddColumn(_actions, "Upstream/creator", "Identity", 510, true);
        AddColumn(_actions, "Status", "Status", 230);

        _evidence = MakeTextViewer(10.5F);
        _evidence.Dock = DockStyle.Fill;
        _evidence.Padding = new Padding(20);

        evidence.Controls.Add(_evidence);

        var footerBar = new Panel { Dock = DockStyle.Fill, BackColor = Base };
        _footer = MakeLabel("", 9.1F, false, TextMuted);
        _footer.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _footer.SetBounds(4, 5, 820, 33);
        footerBar.Controls.Add(_footer);
        var refresh = MakeButton("Refresh files", (_, _) => RefreshData());
        refresh.Size = new Size(155, 34);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        footerBar.Controls.Add(refresh);
        footerBar.SizeChanged += (_, _) => {
            refresh.Location = new Point(Math.Max(0, footerBar.Width - 160), 4);
            _footer.Width = Math.Max(180, footerBar.Width - 180);
        };
        root.Controls.Add(footerBar, 0, 3);
    }

    private static TabPage MakeTab(string title)
    {
        return new TabPage(title) {
            BackColor = Surface, ForeColor = TextMain,
            Padding = new Padding(10),
            UseVisualStyleBackColor = false,
        };
    }

    private static void AddColumn(DataGridView grid, string title, string property, int width, bool fill = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = title, DataPropertyName = property,
            Width = width, MinimumWidth = 65,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill
                                : DataGridViewAutoSizeColumnMode.None,
        });
    }

    private static (DataGridView Grid, TextBox Search, ComboBox Filter)
        MakeGridPane(TabPage tab, string placeholder, bool addFilter)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill, BackColor = Surface,
            RowCount = 2, ColumnCount = 1, Padding = new Padding(5),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tab.Controls.Add(panel);

        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            BackColor = Surface, WrapContents = false,
        };
        var search = new TextBox {
            Width = 410, Height = 32, Font = new Font("Segoe UI", 11),
            BackColor = Color.FromArgb(35, 48, 69), ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle, PlaceholderText = placeholder,
            Margin = new Padding(6, 6, 12, 6),
        };
        bar.Controls.Add(search);
        var filter = new ComboBox {
            Width = 240, Height = 32, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(35, 48, 69), ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5F),
            Margin = new Padding(0, 6, 8, 6),
        };
        if (addFilter) bar.Controls.Add(filter);
        panel.Controls.Add(bar, 0, 0);

        var grid = new DataGridView {
            Dock = DockStyle.Fill, AutoGenerateColumns = false,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false, BackgroundColor = Surface,
            BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(48, 62, 84),
            ColumnHeadersHeight = 42, RowTemplate = { Height = 39 },
            EnableHeadersVisualStyles = false, ScrollBars = ScrollBars.Both,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            Font = new Font("Segoe UI", 10.5F),
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 61, 85);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
        grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 34, 55);
        grid.DefaultCellStyle.ForeColor = TextMain;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(41, 87, 124);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 42, 62);
        grid.AlternatingRowsDefaultCellStyle.ForeColor = TextMain;
        panel.Controls.Add(grid, 0, 1);
        return (grid, search, filter);
    }

    private void FilterSources()
    {
        if (_sources is null || _kindFilter is null) return;
        var term = _sourcesSearch.Text.Trim();
        var kind = _kindFilter.SelectedItem?.ToString() ?? "All categories";
        var rows = _state.Sources
            .Where(x => kind == "All categories" || x.Kind == kind)
            .Where(x => term.Length == 0 || (x.PublicUrl + " " + x.Kind + " " + x.SourceAreas)
                .Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Mentions).ThenBy(x => x.PublicUrl).ToList();
        _sources.DataSource = rows;
    }

    private void FilterRepos()
    {
        if (_repos is null || _repoFilter is null) return;
        var term = _githubSearch.Text.Trim();
        var filter = _repoFilter.SelectedItem?.ToString() ?? "All verification states";
        var rows = _state.Repositories
            .Where(x => filter == "All verification states" || x.Status == filter)
            .Where(x => term.Length == 0 || (x.Repository + " " + x.Canonical + " " + x.License)
                .Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Citations).ThenBy(x => x.Repository).ToList();
        _repos.DataSource = rows;
    }

    private static void OpenSafeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void OpenSelectedSource()
    {
        if (_sources.CurrentRow?.DataBoundItem is SourceRow row)
            OpenSafeUrl(row.PublicUrl);
    }

    private void OpenSelectedRepo()
    {
        if (_repos.CurrentRow?.DataBoundItem is RepoRow row)
        {
            var name = row.Canonical is not ("NOT_VERIFIED" or "")
                ? row.Canonical : row.Repository;
            if (name.Count(x => x == '/') == 1)
                OpenSafeUrl("https://github.com/" + name);
        }
    }

    public void SelectPage(int index)
    {
        if (index < 0 || index >= _tabs.TabPages.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _tabs.SelectedIndex = index;
        _tabs.SelectedTab?.PerformLayout();
        Application.DoEvents();
    }
    public void RefreshData()
    {
        _state = AuditSnapshot.Load(_dataDirectory);
        var values = new[] {
            _state.DiscoveredCount, _state.Repositories.Count, _state.VerifiedActiveGithub,
            _state.NewStars, _state.NewFollows,
        };
        for (var i = 0; i < values.Length; i++)
            _metricValues[i].Text = values[i].ToString("N0");

        _metricCaptions[2].Text = "ACTIVE (SAVED)";
        _status.Text = _live is null ? "LOCAL / " + _state.AuditStatus + " / READ ONLY" : "LIVE / " + _live.Status;
        _status.ForeColor = _state.AuditStatus == "PASS" ? Accent : Color.Goldenrod;
        _kindFilter.BeginUpdate();
        var oldKind = _kindFilter.SelectedItem?.ToString() ?? "All categories";
        _kindFilter.Items.Clear();
        _kindFilter.Items.Add("All categories");
        foreach (var kind in _state.SourceKinds.Keys.OrderBy(x => x)) _kindFilter.Items.Add(kind);
        _kindFilter.SelectedItem = _kindFilter.Items.Contains(oldKind) ? oldKind : "All categories";
        _kindFilter.EndUpdate();
        _repoFilter.BeginUpdate();
        var oldRepo = _repoFilter.SelectedItem?.ToString() ?? "All verification states";
        _repoFilter.Items.Clear();
        _repoFilter.Items.Add("All verification states");
        foreach (var status in _state.Repositories.Select(x => x.Status).Distinct().OrderBy(x => x))
            _repoFilter.Items.Add(status);
        _repoFilter.SelectedItem = _repoFilter.Items.Contains(oldRepo) ? oldRepo : "All verification states";
        _repoFilter.EndUpdate();
        FilterSources();
        FilterRepos();
        _actions.DataSource = _state.Engagement.OrderBy(x => x.Action).ThenBy(x => x.Identity).ToList();

        var a = _state.LastAuditUtc;
        _overview.Text =
            "LIVE LOCAL RESEARCH AGENT\n\n" +
            "Session-resident viewer: " + (_live?.Status ?? "QA_ONLY") + "\n" +
            "Approved research roots watched: " + (_live?.WatchedRoots ?? 0) + "\n" +
            "Local file changes observed: " + (_live?.SourceEvents ?? 0) + "\n" +
            "Last successful live check: " + (_live?.LastSuccess?.ToString("O") ?? "NOT_YET_VERIFIED") + "\n" +
            "Local file-change detection and 15-minute GitHub/profile checks.\n" +
            "Tray icon continues monitoring when the window is hidden.\n" +

            "Last audit: " + a + "  |  " + _state.AuditStatus + "\n" +
            "Source discovery: " + _state.ResearchStatus + "\n" +
            "Public repository links verified in audit: " + _state.PublicLinksChecked + "\n" +
            "Tracked pull requests checked: " + _state.PullRequestsChecked + "\n\n" +
            "RESEARCH INDEX\n\n" +
            "Distinct discovered references: " + _state.DiscoveredCount + "\n" +
            "Third-party GitHub references: " + _state.Repositories.Count + "\n" +
            "Active upstreams in saved verification: " + _state.VerifiedActiveGithub + "\n" +
            "GitHub verification snapshot: " + _state.GithubSnapshotUtc + "\n" +
            "One-time social actions in U006: " + _state.NewStars +
            " new stars and " + _state.NewFollows + " new follows.\n\n" +
            "Discovery does not prove software installation, live paper validity,\n" +
            "publishing authority, or owner acceptance. Open-source licensing\n" +
            "must be reviewed before reusing any research software.\n\n" +
            "Native agent refreshes from filesystem events and in-process checks.\n" +
            "No extra service, GitHub write, or automatically approved fork.";
        if (_state.AuditStatus == "SAMPLE_ONLY")
        {
            _overview.Text =
                "OFFLINE DEMO  /  SAMPLE_ONLY\n\n" +
                "No live watcher, GitHub session, account permissions, or research connection.\n" +
                "This window uses clearly labeled example JSON bundled with Source Atlas.\n" +
                "The entries have not been checked against live external services.\n\n" +
                "SAMPLE CATALOG\n\n" +
                "Reference examples: " + _state.DiscoveredCount + "\n" +
                "Sample GitHub records: " + _state.Repositories.Count + "\n" +
                "Verified public repositories: 0\n" +
                "Stars and follows performed: 0\n\n" +
                "Switch tabs to inspect example sources, GitHub metadata, and local hashes.\n" +
                "A real local monitor requires an independently authorized configuration.";
        }
        var lines = new List<string> {
            "LOCAL EVIDENCE  /  SHA-256", "",
            "Audit data is stored on this PC. Source files are read-only.",
            "The source index is private and not automatically published.", "",
        };
        foreach (var row in _state.Evidence) {
            lines.Add(row.File + "  |  " + row.ModifiedLocal + "  |  " +
                      row.SizeBytes.ToString("N0") + " bytes");
            lines.Add("  sha256: " + row.Sha256);
        }
        if (!string.IsNullOrEmpty(_state.RootError)) {
            lines.Add(""); lines.Add("INCOMPLETE INPUTS:");
            lines.Add(_state.RootError);
        }
        _evidence.Text = string.Join(Environment.NewLine, lines);
        _footer.Text = "LOCAL EVIDENCE  /  READ ONLY  /  " + DateTime.Now.ToString("HH:mm:ss");
    }
}
