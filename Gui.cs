using System.Diagnostics;
using System.Media;

namespace PclCeUncut.Patcher;

internal sealed class Gui : Form
{
    private static readonly Color Accent = Color.FromArgb(15, 108, 189);
    private static readonly Color OkColor = Color.FromArgb(14, 124, 60);
    private static readonly Color BadColor = Color.FromArgb(179, 38, 30);
    private static readonly Color MutedColor = Color.FromArgb(96, 96, 96);
    private const int RowHeight = 30;

    private readonly TextBox _input = new();
    private readonly TextBox _output = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();
    private readonly Button _pickInput = new();
    private readonly Button _pickOutput = new();
    private readonly Button _check = new();
    private readonly Button _patch = new();
    private readonly LinkLabel _openFolder = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 400 };

    private bool _busy;
    private bool _canPatch;
    private bool _outputCustom;
    private string? _lastOutput;

    public Gui(string? initialFile)
    {
        Text = "PCL CE Uncut 补丁工具 " + Program.Version;
        ClientSize = new Size(820, 620);
        MinimumSize = new Size(700, 540);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        BackColor = Color.White;
        Font = PickFont();

        BuildLayout();
        WireEvents();

        if (!string.IsNullOrWhiteSpace(initialFile))
            SetInput(initialFile);
    }

    private static Font PickFont()
    {
        var font = new Font("Microsoft YaHei UI", 9F);
        if (string.Equals(font.Name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
            return font;
        return SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(20, 16, 20, 14),
            BackColor = Color.White,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++)
            root.RowStyles.Add(new RowStyle(i == 7 ? SizeType.Percent : SizeType.AutoSize, i == 7 ? 100F : 0F));

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.FromArgb(64, 64, 64),
            Margin = new Padding(0, 0, 0, 8),
            Text = "把 PCL CE 官方发布的 exe 拖进窗口，或者点右边的「选择文件」。\r\n" +
                   "工具只改内嵌主程序里的地区门禁，写出一份和原文件一样大的副本，原文件不会被修改。",
        };
        root.Controls.Add(intro, 0, 0);

        root.Controls.Add(Section("PCL CE 程序"), 0, 1);

        _input.AutoSize = false;
        _input.Height = RowHeight;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.PlaceholderText = @"例如 C:\Users\你\Downloads\PCL2_CE_Release_x64.exe";

        _pickInput.Text = "选择文件…";
        _pickInput.Width = 96;
        _pickInput.Height = RowHeight;
        _pickInput.FlatStyle = FlatStyle.System;

        root.Controls.Add(NewRow(_input, _pickInput), 0, 2);

        _status.AutoSize = true;
        _status.ForeColor = MutedColor;
        _status.Margin = new Padding(0, 8, 0, 8);
        _status.Text = "等待选择文件";
        root.Controls.Add(_status, 0, 3);

        root.Controls.Add(Section("输出文件"), 0, 4);

        _output.AutoSize = false;
        _output.Height = RowHeight;
        _output.BorderStyle = BorderStyle.FixedSingle;
        _output.PlaceholderText = "默认放在原文件旁边，文件名加 -uncut";

        _pickOutput.Text = "另存为…";
        _pickOutput.Width = 96;
        _pickOutput.Height = RowHeight;
        _pickOutput.FlatStyle = FlatStyle.System;

        root.Controls.Add(NewRow(_output, _pickOutput), 0, 5);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 14, 0, 0),
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _check.Text = "仅检查";
        _check.Width = 96;
        _check.Height = 34;
        _check.FlatStyle = FlatStyle.System;
        _check.Anchor = AnchorStyles.Left;
        _check.Margin = new Padding(0, 0, 8, 0);

        _patch.Text = "打补丁";
        _patch.Width = 132;
        _patch.Height = 34;
        _patch.FlatStyle = FlatStyle.Flat;
        _patch.FlatAppearance.BorderSize = 0;
        _patch.BackColor = Accent;
        _patch.ForeColor = Color.White;
        _patch.Font = new Font(Font, FontStyle.Bold);
        _patch.Anchor = AnchorStyles.Right;
        _patch.Margin = new Padding(0);
        _patch.Enabled = false;

        actions.Controls.Add(_check, 0, 0);
        actions.Controls.Add(_patch, 1, 0);
        root.Controls.Add(actions, 0, 6);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.WordWrap = true;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.BackColor = Color.FromArgb(250, 250, 250);
        _log.Font = new Font("Consolas", 9F);
        _log.Margin = new Padding(0, 14, 0, 10);
        root.Controls.Add(_log, 0, 7);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _openFolder.AutoSize = true;
        _openFolder.Text = "打开输出文件夹";
        _openFolder.Enabled = false;
        _openFolder.Margin = new Padding(0, 2, 16, 0);

        var repo = new LinkLabel
        {
            AutoSize = true,
            Text = "GitHub",
            Margin = new Padding(0, 2, 0, 0),
            LinkColor = Accent,
        };
        repo.LinkClicked += (_, _) => OpenUrl("https://github.com/snake-aabb-wtf/PCL-CE-Uncut-Patcher");

        bottom.Controls.Add(_openFolder, 0, 0);
        bottom.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 1, 0);
        bottom.Controls.Add(repo, 2, 0);
        root.Controls.Add(bottom, 0, 8);

        Controls.Add(root);
    }

    private static Label Section(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(32, 32, 32),
        Margin = new Padding(0, 6, 0, 6),
    };

    private static TableLayoutPanel NewRow(Control main, Control button)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        main.Dock = DockStyle.Fill;
        main.Margin = new Padding(0, 0, 8, 0);
        button.Margin = new Padding(0);
        row.Controls.Add(main, 0, 0);
        row.Controls.Add(button, 1, 0);
        return row;
    }

    private void WireEvents()
    {
        _pickInput.Click += (_, _) => PickInput();
        _pickOutput.Click += (_, _) => PickOutput();
        _check.Click += async (_, _) => await InspectAsync(_input.Text.Trim(), true);
        _patch.Click += async (_, _) => await PatchAsync();
        _openFolder.LinkClicked += (_, _) => OpenOutputFolder();

        _input.TextChanged += (_, _) =>
        {
            if (!_outputCustom)
            {
                string text = _input.Text.Trim();
                _output.Text = text.Length > 0 ? Patcher.DefaultOutput(text) : string.Empty;
            }
            if (!_busy)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };

        _output.TextChanged += (_, _) =>
        {
            if (_output.Focused)
                _outputCustom = true;
        };

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await InspectAsync(_input.Text.Trim(), false);
        };

        DragEnter += (_, e) =>
        {
            e.Effect = !_busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };

        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                SetInput(files[0]);
        };

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus("正在处理，等这一轮结束再关窗口", BadColor);
            }
        };
    }

    private void SetInput(string path)
    {
        _outputCustom = false;
        _input.Text = path;
        _canPatch = false;
        SetStatus("正在检查…", MutedColor);
    }

    private void PickInput()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 PCL CE 程序",
            Filter = "PCL CE 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetInput(dialog.FileName);
    }

    private void PickOutput()
    {
        string current = _output.Text.Trim();
        using var dialog = new SaveFileDialog
        {
            Title = "补丁输出到",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            OverwritePrompt = false,
            FileName = current.Length > 0 ? Path.GetFileName(current) : string.Empty,
        };
        string? directory = current.Length > 0 ? Path.GetDirectoryName(current) : null;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            dialog.InitialDirectory = directory;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _output.Text = dialog.FileName;
            _outputCustom = true;
        }
    }

    private async Task InspectAsync(string path, bool announce)
    {
        if (_busy)
            return;

        if (path.Length == 0)
        {
            _canPatch = false;
            SetStatus("等待选择文件", MutedColor);
            return;
        }

        if (!File.Exists(path))
        {
            _canPatch = false;
            SetStatus("找不到文件：" + path, BadColor);
            return;
        }

        SetBusy(true, "正在检查…");
        try
        {
            var info = await Task.Run(() => Patcher.Inspect(path));
            bool patched = info.State == PatchState.Patched;
            _canPatch = !patched;

            SetStatus(
                patched
                    ? "这个文件已经打过补丁了，不需要再处理"
                    : $"✓ 可以打补丁 · PCL CE {info.Version} · {info.EntryCount} 个内嵌文件",
                patched ? BadColor : OkColor);

            if (announce)
            {
                Append($"检查：{Path.GetFileName(path)}（{info.Size:N0} 字节）");
                Append($"PCL CE 版本：{info.Version} · 内嵌文件：{info.EntryCount} 个");
                Append(patched ? "状态：已经打过补丁" : "状态：门禁为原始实现，可以打补丁");
            }
        }
        catch (PatcherException e)
        {
            _canPatch = false;
            SystemSounds.Exclamation.Play();
            SetStatus("× " + e.Message, BadColor);
            if (announce)
                Append("× " + e.Message);
        }
        catch (Exception e)
        {
            _canPatch = false;
            SystemSounds.Exclamation.Play();
            SetStatus("× " + e.GetType().Name + "：" + e.Message, BadColor);
            if (announce)
                Append("× " + e.GetType().Name + "：" + e.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task PatchAsync()
    {
        if (_busy)
            return;

        string input = _input.Text.Trim();
        string output = _output.Text.Trim();

        if (input.Length == 0)
        {
            SetStatus("先选择 PCL CE 的 exe", BadColor);
            return;
        }
        if (!File.Exists(input))
        {
            SetStatus("找不到文件：" + input, BadColor);
            return;
        }
        if (output.Length == 0)
        {
            output = Patcher.DefaultOutput(input);
            _output.Text = output;
        }
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("输出文件不能和输入文件相同", BadColor);
            return;
        }
        if (File.Exists(output))
        {
            var answer = MessageBox.Show(
                this,
                "输出文件已经存在，要覆盖吗？\r\n\r\n" + output,
                "覆盖确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;
        }

        _log.Clear();
        SetBusy(true, "正在打补丁…");
        try
        {
            var result = await Task.Run(() => Patcher.Patch(input, output, Append));
            _lastOutput = output;
            _openFolder.Enabled = true;
            SetStatus($"✓ 完成：{Path.GetFileName(output)}（{result.Size:N0} 字节）", OkColor);

            MessageBox.Show(
                this,
                $"补丁完成。\r\n\r\n输出：{output}\r\n大小：{result.Size:N0} 字节（与原文件相同）\r\nSHA-256：{result.Sha256}\r\n\r\n原文件没有被修改。",
                "PCL CE Uncut",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (PatcherException e)
        {
            Fail(e.Message);
        }
        catch (Exception e)
        {
            Fail(e.GetType().Name + "：" + e.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void Fail(string message)
    {
        SystemSounds.Exclamation.Play();
        SetStatus("× " + message, BadColor);
        Append("× " + message);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _input.Enabled = !busy;
        _output.Enabled = !busy;
        _pickInput.Enabled = !busy;
        _pickOutput.Enabled = !busy;
        _check.Enabled = !busy;
        _patch.Enabled = !busy && _canPatch;
        if (message is not null)
            SetStatus(message, MutedColor);
    }

    private void Append(string line)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(new Action<string>(Append), line);
            return;
        }
        _log.AppendText(line + "\r\n");
    }

    private void OpenOutputFolder()
    {
        if (_lastOutput is null || !File.Exists(_lastOutput))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            SetStatus("× 打不开文件夹：" + e.Message, BadColor);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器就算了
        }
    }
}
