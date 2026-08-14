using System.Diagnostics;
using ClaudePlusPlus.Models;
using ClaudePlusPlus.Services;

namespace ClaudePlusPlus;

internal sealed class MainForm : Form
{
    private readonly Label _packageValue = CreateValueLabel();
    private readonly Label _versionValue = CreateValueLabel();
    private readonly Label _protectionValue = CreateValueLabel();
    private readonly Label _statusValue = CreateValueLabel();
    private readonly TextBox _developerConfigPath = new();
    private readonly Button _launchButton = new();
    private readonly Button _enableButton = new();
    private readonly Button _disableButton = new();
    private readonly Button _openDevToolsButton = new();
    private readonly TextBox _logTextBox = new();

    private ClaudeInstallation? _installation;
    private bool _developerModeEnabled;
    private bool _busy;

    public MainForm()
    {
        Text = "Claude++ · Claude Developer Console";
        MinimumSize = new Size(940, 660);
        Size = new Size(1080, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 246, 243);
        Font = new Font("Segoe UI", 9.5f);

        BuildInterface();
        Shown += async (_, _) => await DetectInstallationAsync();
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.FromArgb(33, 31, 29),
            Padding = new Padding(24, 16, 24, 12)
        };
        header.Controls.Add(new Label
        {
            Text = "Claude++",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 19f),
            AutoSize = true,
            Location = new Point(22, 13)
        });
        header.Controls.Add(new Label
        {
            Text = "启用 Claude 官方内置 Developer Mode，无需 CDP token",
            ForeColor = Color.FromArgb(205, 202, 197),
            AutoSize = true,
            Location = new Point(25, 52)
        });
        Controls.Add(header);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 186));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.BringToFront();

        root.Controls.Add(BuildInstallationPanel(), 0, 0);
        root.Controls.Add(BuildControlPanel(), 0, 1);
        root.Controls.Add(BuildGuidePanel(), 0, 2);
        root.Controls.Add(BuildLogPanel(), 0, 3);
    }

    private Control BuildInstallationPanel()
    {
        var panel = CreateCard();
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(16, 11, 16, 11)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        AddInfoRow(grid, 0, "安装包", _packageValue, "版本", _versionValue);
        AddInfoRow(grid, 1, "调试边界", _protectionValue, "当前状态", _statusValue);
        panel.Controls.Add(grid);
        return panel;
    }

    private Control BuildControlPanel()
    {
        var panel = CreateCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 10),
            ColumnCount = 5,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(CreateCaptionLabel("开发者配置"), 0, 0);
        _developerConfigPath.Dock = DockStyle.Fill;
        _developerConfigPath.ReadOnly = true;
        _developerConfigPath.BackColor = Color.White;
        _developerConfigPath.Margin = new Padding(0, 8, 8, 7);
        layout.Controls.Add(_developerConfigPath, 1, 0);

        _launchButton.Text = "启动 / 唤醒 Claude";
        StyleButton(_launchButton, primary: false);
        _launchButton.Click += async (_, _) => await LaunchNormalAsync();
        layout.Controls.Add(_launchButton, 2, 0);

        _enableButton.Text = "启用并重启";
        StyleButton(_enableButton, primary: true);
        _enableButton.Click += async (_, _) => await EnableAndRestartAsync();
        layout.Controls.Add(_enableButton, 3, 0);

        _openDevToolsButton.Text = "打开 DevTools";
        StyleButton(_openDevToolsButton, primary: true);
        _openDevToolsButton.Click += async (_, _) => await OpenDevToolsAsync();
        layout.Controls.Add(_openDevToolsButton, 4, 0);

        layout.Controls.Add(CreateCaptionLabel("官方快捷键"), 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "Ctrl + Alt + I（打开或聚焦独立 DevTools 窗口）",
            ForeColor = Color.FromArgb(74, 70, 66),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 1, 1);

        var boundary = new Label
        {
            Text = "内置控制台无需 token；外部 CDP 端口仍受签名保护。",
            ForeColor = Color.FromArgb(130, 91, 45),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        layout.Controls.Add(boundary, 2, 1);
        layout.SetColumnSpan(boundary, 2);

        _disableButton.Text = "关闭开发者模式";
        StyleButton(_disableButton, primary: false);
        _disableButton.Click += async (_, _) => await DisableDeveloperModeAsync();
        layout.Controls.Add(_disableButton, 4, 1);

        panel.Controls.Add(layout);
        return panel;
    }

    private static Control BuildGuidePanel()
    {
        var panel = CreateCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(14, 12, 14, 12)
        };
        for (var index = 0; index < 4; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "打开后建议这样看 Claude Code / Cowork 的交互",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Color.FromArgb(45, 42, 39),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.SetColumnSpan(layout.Controls[^1], 4);

        layout.Controls.Add(CreateGuide("Console", "观察前端事件、IPC 报错、功能开关与状态流。"), 0, 1);
        layout.Controls.Add(CreateGuide("Network", "查看 Claude API、SSE、MCP 与附件请求时序。"), 1, 1);
        layout.Controls.Add(CreateGuide("Sources", "搜索 UI 文案、命令名称和交互入口。"), 2, 1);
        layout.Controls.Add(CreateGuide("Elements", "检查 Cowork / Code 页面的 DOM、布局与可访问性。"), 3, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildLogPanel()
    {
        var panel = CreateCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12, 8, 12, 10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "运行日志",
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(45, 42, 39),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.BackColor = Color.FromArgb(31, 30, 29);
        _logTextBox.ForeColor = Color.FromArgb(229, 226, 221);
        _logTextBox.Font = new Font("Cascadia Mono", 9f);
        layout.Controls.Add(_logTextBox, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private async Task DetectInstallationAsync()
    {
        await RunBusyAsync(async () =>
        {
            SetStatus("正在检测 Claude…", Color.FromArgb(163, 102, 34));
            _installation = await ClaudeInstallationDetector.DetectAsync();
            _packageValue.Text = _installation.PackageFullName;
            _versionValue.Text = _installation.Version;
            _developerConfigPath.Text = DeveloperModeService.GetConfigPath(_installation.UserDataPath);
            Log($"检测到 {_installation.PackageFullName}");
            Log($"安装目录：{_installation.InstallLocation}");

            var inspection = await Task.Run(() => AsarInspector.Inspect(_installation.AsarPath));
            _versionValue.Text = $"{_installation.Version}（应用 {inspection.ProductVersion}）";
            _protectionValue.Text = inspection.RequiresSignedCdpToken
                ? "内置 DevTools 可用；外部 CDP 需签名"
                : "版本行为可能已变化";

            _developerModeEnabled = DeveloperModeService.IsEnabled(_installation.UserDataPath);
            SetStatus(
                _developerModeEnabled ? "开发者模式已启用" : "开发者模式未启用",
                _developerModeEnabled ? Color.FromArgb(53, 118, 78) : Color.FromArgb(163, 102, 34));
            Log(_developerModeEnabled
                ? "developer_settings.json 已启用 allowDevTools。"
                : "可一键启用 Claude 官方 allowDevTools 配置，无需 token。");

            TrySetWindowIcon(_installation.ExecutablePath);
        });
    }

    private async Task LaunchNormalAsync()
    {
        if (_installation is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var processId = await Task.Run(() =>
                PackageActivationService.Activate(_installation.AppUserModelId, string.Empty));
            Log($"已启动或唤醒 Claude，激活 PID {processId}。");
            SetStatus(
                _developerModeEnabled ? "Claude 已运行 · 开发者模式已启用" : "Claude 已运行",
                Color.FromArgb(53, 118, 78));
        });
    }

    private async Task EnableAndRestartAsync()
    {
        if (_installation is null)
        {
            return;
        }

        var running = ClaudeProcessService.FindOwnedProcesses(_installation.ExecutablePath);
        var runningCount = running.Count;
        foreach (var process in running)
        {
            process.Dispose();
        }

        if (runningCount > 0 && MessageBox.Show(
                this,
                "启用后需要重启 Claude，正在执行的 Claude Code / Cowork 任务会中断。是否继续？",
                "启用开发者模式",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            DeveloperModeService.SetEnabled(_installation.UserDataPath, enabled: true);
            _developerModeEnabled = true;
            Log("已写入官方 developer_settings.json：allowDevTools=true。");

            var stopped = await ClaudeProcessService.StopOwnedProcessesAsync(_installation.ExecutablePath);
            if (stopped > 0)
            {
                Log($"已停止 {stopped} 个路径验证通过的 Claude 进程。");
            }

            await Task.Delay(700);
            var processId = PackageActivationService.Activate(_installation.AppUserModelId, string.Empty);
            Log($"已重新启动 Claude，激活 PID {processId}。");
            SetStatus("开发者模式已启用 · 正在等待窗口", Color.FromArgb(53, 118, 78));

            await WaitForMainWindowAsync(_installation.ExecutablePath, TimeSpan.FromSeconds(15));
            await ClaudeDevToolsService.OpenOrFocusAsync(_installation.ExecutablePath);
            SetStatus("开发者模式已启用 · DevTools 已打开", Color.FromArgb(53, 118, 78));
            Log("已调用 Claude 官方快捷键 Ctrl+Alt+I 打开 DevTools。");
        });
    }

    private async Task DisableDeveloperModeAsync()
    {
        if (_installation is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await Task.Run(() => DeveloperModeService.SetEnabled(_installation.UserDataPath, enabled: false));
            _developerModeEnabled = false;
            SetStatus("开发者模式已关闭 · 重启 Claude 后完全生效", Color.FromArgb(163, 102, 34));
            Log("已设置 allowDevTools=false；现有 DevTools 窗口不被强制关闭。重启 Claude 后菜单入口消失。");
        });
    }

    private async Task OpenDevToolsAsync()
    {
        if (_installation is null)
        {
            return;
        }

        if (!_developerModeEnabled)
        {
            MessageBox.Show(
                this,
                "请先点击“启用并重启”。Claude 正式版只有在 allowDevTools=true 时才注册 Ctrl+Alt+I。",
                "开发者模式未启用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var processes = ClaudeProcessService.FindOwnedProcesses(_installation.ExecutablePath);
            var isRunning = processes.Any(process => process.MainWindowHandle != IntPtr.Zero);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (!isRunning)
            {
                PackageActivationService.Activate(_installation.AppUserModelId, string.Empty);
                await WaitForMainWindowAsync(_installation.ExecutablePath, TimeSpan.FromSeconds(15));
            }

            await ClaudeDevToolsService.OpenOrFocusAsync(_installation.ExecutablePath);
            SetStatus("DevTools 已打开或已聚焦", Color.FromArgb(53, 118, 78));
            Log("已调用 Claude 官方快捷键 Ctrl+Alt+I。");
        });
    }

    private static async Task WaitForMainWindowAsync(string executablePath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var processes = ClaudeProcessService.FindOwnedProcesses(executablePath);
            try
            {
                if (processes.Any(process => process.MainWindowHandle != IntPtr.Zero))
                {
                    return;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            await Task.Delay(400);
        }

        throw new TimeoutException("Claude 主窗口在 15 秒内没有出现。");
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtonState();
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (Exception error)
        {
            SetStatus("操作失败", Color.FromArgb(174, 52, 52));
            Log($"错误：{error.Message}");
            MessageBox.Show(this, error.Message, "Claude++", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _busy = false;
            UpdateButtonState();
        }
    }

    private void UpdateButtonState()
    {
        var ready = !_busy && _installation is not null;
        _launchButton.Enabled = ready;
        _enableButton.Enabled = ready;
        _disableButton.Enabled = ready && _developerModeEnabled;
        _openDevToolsButton.Enabled = ready && _developerModeEnabled;
    }

    private void SetStatus(string text, Color color)
    {
        _statusValue.Text = text;
        _statusValue.ForeColor = color;
    }

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void TrySetWindowIcon(string executablePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            Icon = icon is null ? null : (Icon)icon.Clone();
        }
        catch
        {
            // The launcher remains usable without an icon.
        }
    }

    private static Control CreateGuide(string title, string description)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(249, 248, 246),
            Margin = new Padding(4),
            Padding = new Padding(12)
        };
        panel.Controls.Add(new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(94, 89, 84),
            Padding = new Padding(0, 29, 0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Color.FromArgb(211, 91, 55)
        });
        return panel;
    }

    private static Panel CreateCard() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        Margin = new Padding(0, 0, 0, 10),
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Label CreateValueLabel() => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(58, 55, 51)
    };

    private static Label CreateCaptionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(105, 101, 96)
    };

    private static void AddInfoRow(
        TableLayoutPanel grid,
        int row,
        string leftCaption,
        Label leftValue,
        string rightCaption,
        Label rightValue)
    {
        grid.Controls.Add(CreateCaptionLabel(leftCaption), 0, row);
        grid.Controls.Add(leftValue, 1, row);
        grid.Controls.Add(CreateCaptionLabel(rightCaption), 2, row);
        grid.Controls.Add(rightValue, 3, row);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(6, 6, 0, 6);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.BackColor = primary ? Color.FromArgb(211, 91, 55) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(54, 50, 46);
        button.Cursor = Cursors.Hand;
    }
}
