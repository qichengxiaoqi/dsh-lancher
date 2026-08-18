using System.Diagnostics;
using System.Drawing;
using DshPlusPlus.Core.Models;
using DshPlusPlus.Core.Services;
using DshPlusPlus.UI.Controls;
using DshPlusPlus.UI.Theme;

namespace DshPlusPlus.UI.Pages;

public sealed class DeepSeekApiPage : PageBase
{
    private const string ApiKeyName = "DEEPSEEK_API_KEY";
    private LauncherSettings _settings;
    private LauncherText _text;
    private readonly DshCredentialStore _credentialStore;
    private readonly IDeepSeekApiClient _apiClient;
    private readonly TextBox _keyBox = new();
    private readonly Label _keyStatus;
    private readonly Label _connectionStatus;
    private readonly Label _balanceStatus;
    private readonly ListBox _models = new();
    private Label? _keyLabel;
    private Label? _authCardTitle;
    private Label? _connectionTitle;
    private Label? _balanceTitle;
    private Label? _note;
    private GlowButton? _saveButton;
    private GlowButton? _clearButton;
    private GlowButton? _githubButton;
    private GlowButton? _consoleButton;
    private GlowButton? _testButton;
    private GlowButton? _balanceButton;
    private GlowButton? _modelsButton;

    public DeepSeekApiPage(
        LauncherSettings settings,
        DshCredentialStore credentialStore,
        IDeepSeekApiClient apiClient,
        ThemeManager theme,
        LauncherText? text = null)
        : base(
            theme,
            LauncherTextCatalog.Get(settings.Language).DeepSeekApi,
            LauncherTextCatalog.Get(settings.Language).Pick(
                "管理 API Key、连接质量、可用模型和账户余额。",
                "Manage the API key, connection quality, available models and account balance."))
    {
        _settings = settings;
        _text = text ?? LauncherTextCatalog.Get(settings.Language);
        _credentialStore = credentialStore;
        _apiClient = apiClient;
        _keyStatus = MutedLabel(string.Empty);
        _connectionStatus = MutedLabel(_text.Pick("尚未检测连接", "Connection has not been tested"));
        _balanceStatus = MutedLabel(_text.Pick("尚未查询余额", "Balance has not been queried"));
        Build();
        RefreshCredentialStatus();
    }

    public void UpdateSettings(LauncherSettings settings)
    {
        _settings = settings;
        _keyBox.Clear();
        RefreshCredentialStatus();
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.AutoSize);
        layout.RowStyles[2] = new RowStyle(SizeType.Percent, 100);

        var auth = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Palette.Surface
        };
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var keyHeight = UiMetrics.PixelsFromDip(38, DeviceDpi, Theme.Settings.FontScale);
        auth.RowStyles.Add(new RowStyle(SizeType.Absolute, keyHeight));
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _keyLabel = new Label { Text = _text.Pick("本地 API Key", "Local API key"), Dock = DockStyle.Top, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" };
        auth.Controls.Add(_keyLabel, 0, 0);
        var keyLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 2),
            BackColor = Theme.Palette.Surface
        };
        keyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        keyLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        keyLine.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _keyBox.Dock = DockStyle.Fill;
        _keyBox.MinimumSize = new Size(
            UiMetrics.PixelsFromDip(UiMetrics.ApiKeyInputMinimumDip, DeviceDpi, Theme.Settings.FontScale),
            Math.Max(24, keyHeight - 8));
        _keyBox.PasswordChar = '●';
        _keyBox.Margin = new Padding(0, 2, 8, 2);
        _saveButton = new GlowButton(_text.Pick("保存到 DSH", "Save to DSH"), Theme.Palette, primary: true);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Click += SaveKeyAsync;
        _clearButton = new GlowButton(_text.Pick("清除", "Clear"), Theme.Palette);
        _clearButton.Dock = DockStyle.Fill;
        _clearButton.Click += ClearKeyAsync;
        keyLine.Controls.Add(_keyBox, 0, 0);
        keyLine.Controls.Add(_saveButton, 1, 0);
        keyLine.Controls.Add(_clearButton, 2, 0);
        auth.Controls.Add(keyLine, 0, 1);
        _keyStatus.AutoSize = true;
        _keyStatus.Dock = DockStyle.Top;
        auth.Controls.Add(_keyStatus, 0, 2);
        var links = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 4, 0, 0)
        };
        _githubButton = new GlowButton(_text.Pick("GitHub 仓库", "GitHub repository"), Theme.Palette);
        _githubButton.Click += (_, _) => OpenLink("https://github.com/deepseek-ai/deepseek-harness");
        _consoleButton = new GlowButton(_text.Pick("DeepSeek 控制台", "DeepSeek console"), Theme.Palette);
        _consoleButton.Click += (_, _) => OpenLink("https://platform.deepseek.com/usage");
        links.Controls.AddRange([_githubButton, _consoleButton]);
        auth.Controls.Add(links, 0, 3);
        auth.MinimumSize = new Size(0, UiMetrics.PixelsFromDip(132, DeviceDpi, Theme.Settings.FontScale));
        var authCard = Card(auth, _text.Pick("凭据与入口", "Credentials and links"));
        _authCardTitle = authCard.Controls.OfType<Label>().FirstOrDefault();
        authCard.MinimumSize = new Size(0, UiMetrics.PixelsFromDip(190, DeviceDpi, Theme.Settings.FontScale));
        layout.Controls.Add(authCard, 0, 1);

        var resultGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Palette.Background
        };
        resultGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        resultGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        resultGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        resultGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        resultGrid.Controls.Add(BuildConnectionCard(), 0, 0);
        resultGrid.Controls.Add(BuildBalanceCard(), 1, 0);
        resultGrid.Controls.Add(BuildModelsCard(), 0, 1);
        var note = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Theme.Palette.Surface };
        _note = MutedLabel(_text.Pick("连接检测默认调用 /models，不执行收费对话请求。余额查询使用 /user/balance。", "Connection tests call /models and never send billable chat requests. Balance uses /user/balance."));
        note.Controls.Add(_note);
        resultGrid.Controls.Add(note, 1, 1);
        layout.Controls.Add(resultGrid, 0, 2);
        Controls.Add(layout);
    }

    private Control BuildConnectionCard()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _connectionTitle = new Label { Text = _text.Pick("API 通道检测", "API connection"), Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" };
        panel.Controls.Add(_connectionTitle, 0, 0);
        _testButton = new GlowButton(_text.Pick("测试连接", "Test connection"), Theme.Palette, primary: true);
        _testButton.Click += TestConnectionAsync;
        panel.Controls.Add(_testButton, 0, 1);
        panel.Controls.Add(_connectionStatus, 0, 2);
        return panel;
    }

    private Control BuildBalanceCard()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _balanceTitle = new Label { Text = _text.Pick("账户余额", "Account balance"), Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" };
        panel.Controls.Add(_balanceTitle, 0, 0);
        _balanceButton = new GlowButton(_text.Pick("查询余额", "Query balance"), Theme.Palette);
        _balanceButton.Click += QueryBalanceAsync;
        panel.Controls.Add(_balanceButton, 0, 1);
        panel.Controls.Add(_balanceStatus, 0, 2);
        return panel;
    }

    private Control BuildModelsCard()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        _modelsButton = new GlowButton(_text.Pick("刷新模型", "Refresh models"), Theme.Palette) { Dock = DockStyle.Top };
        _modelsButton.Click += LoadModelsAsync;
        _models.Dock = DockStyle.Fill;
        _models.BorderStyle = BorderStyle.FixedSingle;
        _models.IntegralHeight = false;
        panel.Controls.Add(_models);
        panel.Controls.Add(_modelsButton);
        return panel;
    }

    public override void ApplyLanguage(LauncherText text)
    {
        _text = text;
        ApplyHeader(
            text.DeepSeekApi,
            text.Pick(
                "管理 API Key、连接质量、可用模型和账户余额。",
                "Manage the API key, connection quality, available models and account balance."));
        if (_keyLabel is not null)
            _keyLabel.Text = text.Pick("本地 API Key", "Local API key");
        if (_authCardTitle is not null)
            _authCardTitle.Text = text.Pick("凭据与入口", "Credentials and links");
        if (_connectionTitle is not null)
            _connectionTitle.Text = text.Pick("API 通道检测", "API connection");
        if (_balanceTitle is not null)
            _balanceTitle.Text = text.Pick("账户余额", "Account balance");
        if (_note is not null)
            _note.Text = text.Pick("连接检测默认调用 /models，不执行收费对话请求。余额查询使用 /user/balance。", "Connection tests call /models and never send billable chat requests. Balance uses /user/balance.");
        if (_saveButton is not null)
            _saveButton.Text = text.Pick("保存到 DSH", "Save to DSH");
        if (_clearButton is not null)
            _clearButton.Text = text.Pick("清除", "Clear");
        if (_githubButton is not null)
            _githubButton.Text = text.Pick("GitHub 仓库", "GitHub repository");
        if (_consoleButton is not null)
            _consoleButton.Text = text.Pick("DeepSeek 控制台", "DeepSeek console");
        if (_testButton is not null)
            _testButton.Text = text.Pick("测试连接", "Test connection");
        if (_balanceButton is not null)
            _balanceButton.Text = text.Pick("查询余额", "Query balance");
        if (_modelsButton is not null)
            _modelsButton.Text = text.Pick("刷新模型", "Refresh models");
        if (_connectionStatus.Text is "尚未检测连接" or "Connection has not been tested")
            _connectionStatus.Text = text.Pick("尚未检测连接", "Connection has not been tested");
        if (_balanceStatus.Text is "尚未查询余额" or "Balance has not been queried")
            _balanceStatus.Text = text.Pick("尚未查询余额", "Balance has not been queried");
        RefreshCredentialStatus();
    }

    private void RefreshCredentialStatus()
    {
        var status = _credentialStore.ReadStatus(_settings.Paths.CredentialsFile, ApiKeyName);
        var source = status.HasEnvironmentOverride
            ? _text.Pick("环境变量优先", "Environment variable overrides")
            : _text.Pick("本地文件", "Local file");
        _keyStatus.Text = $"{LocalizeCredentialMessage(status.Message)} · {status.MaskedValue} · {source}";
        _keyStatus.ForeColor = status.HasEnvironmentOverride ? Theme.Palette.Warning : Theme.Palette.Muted;
    }

    private async void SaveKeyAsync(object? sender, EventArgs e)
    {
        try
        {
            await _credentialStore.SetAsync(_settings.Paths.CredentialsFile, ApiKeyName, _keyBox.Text.Trim(), CancellationToken.None);
            _keyBox.Clear();
            RefreshCredentialStatus();
        }
        catch (Exception ex)
        {
            _keyStatus.Text = _text.Pick($"保存失败：{ex.Message}", $"Save failed: {ex.Message}");
            _keyStatus.ForeColor = Theme.Palette.Danger;
        }
    }

    private async void ClearKeyAsync(object? sender, EventArgs e)
    {
        await _credentialStore.ClearAsync(_settings.Paths.CredentialsFile, ApiKeyName, CancellationToken.None);
        RefreshCredentialStatus();
    }

    private async void TestConnectionAsync(object? sender, EventArgs e)
    {
        var key = _credentialStore.ReadSecret(_settings.Paths.CredentialsFile, ApiKeyName);
        var result = await _apiClient.TestConnectionAsync(key ?? string.Empty, CancellationToken.None);
        _connectionStatus.Text = $"{result.Message} · {result.LatencyMs} ms · HTTP {result.StatusCode?.ToString() ?? "-"}";
        _connectionStatus.ForeColor = result.Success ? Theme.Palette.Success : Theme.Palette.Danger;
    }

    private async void QueryBalanceAsync(object? sender, EventArgs e)
    {
        var key = _credentialStore.ReadSecret(_settings.Paths.CredentialsFile, ApiKeyName);
        try
        {
            var result = await _apiClient.GetBalanceAsync(key ?? string.Empty, CancellationToken.None);
            _balanceStatus.Text = result.Balances.Count == 0
                ? result.Message
                : string.Join("   ", result.Balances.Select(balance => $"{balance.Currency} {balance.TotalBalance:0.####}"));
            _balanceStatus.ForeColor = result.IsAvailable ? Theme.Palette.Success : Theme.Palette.Warning;
        }
        catch (Exception ex)
        {
            _balanceStatus.Text = _text.Pick($"查询失败：{ex.Message}", $"Query failed: {ex.Message}");
            _balanceStatus.ForeColor = Theme.Palette.Danger;
        }
    }

    private async void LoadModelsAsync(object? sender, EventArgs e)
    {
        var key = _credentialStore.ReadSecret(_settings.Paths.CredentialsFile, ApiKeyName);
        try
        {
            var models = await _apiClient.GetModelsAsync(key ?? string.Empty, CancellationToken.None);
            _models.Items.Clear();
            foreach (var model in models)
                _models.Items.Add($"{model.Id}  ·  {model.OwnedBy}");
        }
        catch (Exception ex)
        {
            _models.Items.Clear();
            _models.Items.Add(_text.Pick($"加载失败：{ex.Message}", $"Load failed: {ex.Message}"));
        }
    }

    private static void OpenLink(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private string LocalizeCredentialMessage(string message) => _text.IsEnglish
        ? message switch
        {
            "当前由环境变量覆盖" => "Overridden by environment variable",
            "已配置" => "Configured",
            "未配置" => "Not configured",
            "未设置" => "Not set",
            _ => message
        }
        : message;
}
