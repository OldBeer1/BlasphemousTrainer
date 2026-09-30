using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class Installer : Form
{
    private readonly ComboBox directory = new ComboBox();
    private readonly TextBox log = new TextBox();
    private readonly CheckBox removeConfig = new CheckBox();
    private readonly List<Button> actions = new List<Button>();
    private bool busy;
    private readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;

    [STAThread] private static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new Installer())
        {
            if (args.Length == 2 && args[0] == "--render")
            { form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Application.DoEvents(); using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(args[1]); } return; }
            Application.Run(form);
        }
    }
    private Installer()
    {
        Text = "神之亵渎修改器 · 安装与维护 v" + BlasphemousTrainer.ProjectInfo.Version;
        ClientSize = new Size(820, 570); MinimumSize = new Size(820, 570);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "安装一次，以后直接启动游戏，按 F1 打开面板。", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 14, FontStyle.Bold) });
        layout.Controls.Add(new Label { Text = "目标游戏目录（先退出游戏；可自动发现或手动选择 Blasphemous.exe）", Dock = DockStyle.Fill });
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        directory.Dock = DockStyle.Fill; pathRow.Controls.Add(directory);
        var browse = new Button { Text = "选择游戏…", Dock = DockStyle.Fill }; browse.Click += delegate { using (var picker = new OpenFileDialog { Filter = "Blasphemous.exe|Blasphemous.exe", Title = "选择游戏程序" }) if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = Path.GetDirectoryName(picker.FileName); }; pathRow.Controls.Add(browse); actions.Add(browse); layout.Controls.Add(pathRow);
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddAction(row, "检查／预览", "Check"); AddAction(row, "安装／更新", "Install"); AddAction(row, "备份存档", "Backup"); AddAction(row, "卸载本插件", "Uninstall"); layout.Controls.Add(row);
        removeConfig.Text = "卸载时同时移除本插件配置（默认保留；会先备份）"; removeConfig.Dock = DockStyle.Fill; layout.Controls.Add(removeConfig);
        log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.BackColor = Color.White;
        log.Text = "等待操作。\r\n\r\n首次准备加载器需要联网，从官方固定版本下载并校验。\r\n安装前会备份已发现的默认存档；不会自动恢复存档或调整云同步。\r\n卸载仅处理本插件，保留加载器、其他 MOD 和存档。\r\n\r\n文件安装完成 ≠ 游戏内功能测试通过。"; layout.Controls.Add(log);
        layout.Controls.Add(new Label { Text = "需要 Windows PowerShell 5.1 与 .NET Framework 4.5+；无需 SDK。\r\n使用说明见 README.md。改动前请先自行备份存档。", Dock = DockStyle.Fill });
        Discover(); FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; MessageBox.Show(this, "正在执行文件操作，请等待结束。", "稍等"); } };
    }
    private void AddAction(FlowLayoutPanel row, string title, string mode)
    { var button = new Button { Text = title, Width = 160, Height = 38 }; button.Click += delegate { Run(mode); }; row.Controls.Add(button); actions.Add(button); }
    private void Discover()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(baseDir);
        while (current != null) { roots.Add(current.FullName); current = current.Parent; }
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!String.IsNullOrEmpty(steam))
        {
            roots.Add(Path.Combine(steam, @"steamapps\common\Blasphemous"));
            string libraries = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libraries)) foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"")) roots.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps\common\Blasphemous"));
        }
        foreach (string root in roots) if (File.Exists(Path.Combine(root, "Blasphemous.exe"))) directory.Items.Add(root);
        if (directory.Items.Count == 1) directory.SelectedIndex = 0;
        else if (directory.Items.Count > 1) log.AppendText("\r\n发现多个位置，请从下拉列表选择目标。\r\n");
    }
    private async void Run(string mode)
    {
        string target = directory.Text.Trim();
        if (!File.Exists(Path.Combine(target, "Blasphemous.exe"))) { MessageBox.Show(this, "请先选择正确的游戏目录。", "目录未确认"); return; }
        busy = true; foreach (var button in actions) button.Enabled = false; directory.Enabled = false; removeConfig.Enabled = false;
        log.Text = "目标：" + target + "\r\n正在" + mode + "，请等待……\r\n";
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"));
            info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Path.Combine(baseDir, "Backend.ps1")) + " -Mode " + mode + " -GameRoot " + Quote(target) + (removeConfig.Checked ? " -RemoveConfig" : "");
            info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
            using (var process = Process.Start(info))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(); Task<string> error = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(output, error); await Task.Run(() => process.WaitForExit());
                log.AppendText(output.Result + error.Result + "\r\n" + (process.ExitCode == 0 ? "操作结束。" : "操作未完成，请查看原因。"));
            }
        }
        catch (Exception e) { log.AppendText("操作失败：" + e.Message); }
        finally { busy = false; foreach (var button in actions) button.Enabled = true; directory.Enabled = true; removeConfig.Enabled = true; }
    }
    private static string Quote(string value) { return "\"" + value.Replace("\"", "") .TrimEnd('\\') + "\""; }
}
