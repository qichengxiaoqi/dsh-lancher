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
    private readonly LauncherSettings _settings;
    private readonly DshCredentialStore _credentialStore;
    private readonly IDeepSeekApiClient _apiClient;
    private readonly TextBox _keyBox = new();
    private readonly Label _keyStatus;
    private readonly Label _connectionStatus;
    private readonly Label _balanceStatus;
    private readonly ListBox _models = new();

    public DeepSeekApiPage(
        LauncherSettings settings,
        DshCredentialStore credentialStore,
        IDeepSeekApiClient apiClient,
        ThemeManager theme)
        : base(theme, "DeepSeek API", "管理 API Key、连接质量、可用模型和账户余额。")
    {
        _settings = settings;
        _credentialStore = credentialStore;
        _apiClient = apiClient;
        _keyStatus = MutedLabel(string.Empty);
        _connectionStatus = MutedLabel("尚未检测连接");
        _balanceStatus = MutedLabel("尚未查询余额");
        Build();
        RefreshCredentialStatus();
    }

    private void Build()
    {
        var layout = CreatePageLayout(3);
        layout.RowStyles[1] = new RowStyle(SizeType.AutoSize);
        layout.RowStyles[2] = new RowStyle(SizeType.Percent, 100);

        var auth = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            BackColor = Theme.Palette.Surface
        };
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        auth.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var keyLabel = new Label { Text = "本地 API Key", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" };
        auth.Controls.Add(keyLabel, 0, 0);
        var keyLine = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _keyBox.MinimumSize = new Size(220, 0);
        _keyBox.Width = 340;
        _keyBox.PasswordChar = '●';
        _keyBox.Margin = new Padding(0, 3, 8, 3);
        var save = new GlowButton("保存到 DSH", Theme.Palette, primary: true);
        save.Click += SaveKeyAsync;
        var clear = new GlowButton("清除", Theme.Palette);
        clear.Click += ClearKeyAsync;
        keyLine.Controls.AddRange([_keyBox, save, clear]);
        auth.Controls.Add(keyLine, 0, 1);
        auth.Controls.Add(_keyStatus, 0, 2);
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true };
        var github = new GlowButton("GitHub 仓库", Theme.Palette);
        github.Click += (_, _) => OpenLink("https://github.com/deepseek-ai/deepseek-harness");
        var console = new GlowButton("DeepSeek 控制台", Theme.Palette);
        console.Click += (_, _) => OpenLink("https://platform.deepseek.com/usage");
        links.Controls.AddRange([github, console]);
        auth.Controls.Add(links, 0, 3);
        layout.Controls.Add(Card(auth, "凭据与入口"), 0, 1);

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
        note.Controls.Add(MutedLabel("连接检测默认调用 /models，不执行收费对话请求。余额查询使用 /user/balance。"));
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
        panel.Controls.Add(new Label { Text = "API 通道检测", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" }, 0, 0);
        var button = new GlowButton("测试连接", Theme.Palette, primary: true);
        button.Click += TestConnectionAsync;
        panel.Controls.Add(button, 0, 1);
        panel.Controls.Add(_connectionStatus, 0, 2);
        return panel;
    }

    private Control BuildBalanceCard()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "账户余额", Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Palette.Text, Tag = "section" }, 0, 0);
        var button = new GlowButton("查询余额", Theme.Palette);
        button.Click += QueryBalanceAsync;
        panel.Controls.Add(button, 0, 1);
        panel.Controls.Add(_balanceStatus, 0, 2);
        return panel;
    }

    private Control BuildModelsCard()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Palette.Surface, Padding = new Padding(16) };
        var button = new GlowButton("刷新模型", Theme.Palette) { Dock = DockStyle.Top };
        button.Click += LoadModelsAsync;
        _models.Dock = DockStyle.Fill;
        _models.BorderStyle = BorderStyle.FixedSingle;
        _models.IntegralHeight = false;
        panel.Controls.Add(_models);
        panel.Controls.Add(button);
        return panel;
    }

    private void RefreshCredentialStatus()
    {
        var status = _credentialStore.ReadStatus(_settings.Paths.CredentialsFile, ApiKeyName);
        _keyStatus.Text = $"{status.Message} · {status.MaskedValue} · {status.HasEnvironmentOverride switch { true => "环境变量优先", false => "本地文件" }}";
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
            _keyStatus.Text = $"保存失败：{ex.Message}";
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
            _balanceStatus.Text = $"查询失败：{ex.Message}";
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
            _models.Items.Add($"加载失败：{ex.Message}");
        }
    }

    private static void OpenLink(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
