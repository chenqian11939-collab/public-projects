using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("顺序贴安装向导")]
[assembly: AssemblyProduct("顺序贴")]
[assembly: AssemblyVersion("1.3.2.0")]
[assembly: AssemblyFileVersion("1.3.2.0")]

namespace SequencePasteSetup
{
    internal static class Installer
    {
        internal static string InstallTo(string requestedDirectory, bool desktopShortcut)
        {
            string directory = InstallCommon.FullDirectory(requestedDirectory);
            bool containsOnlyData = Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).All(x => Directory.Exists(x) && String.Equals(Path.GetFileName(x), "Data", StringComparison.OrdinalIgnoreCase));
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any() && !InstallCommon.IsInstalled(directory) && !containsOnlyData)
                throw new IOException("这个目录已有其他文件。请选择一个空文件夹，或选择已由本安装包安装的顺序贴目录。");
            InstallCommon.EnsureNotRunning(directory);
            Directory.CreateDirectory(directory);
            string stage = Path.Combine(directory, ".安装临时-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var committed = new List<string>(); var backups = new List<string>();
            var names = InstallCommon.Files.Concat(new[] { InstallCommon.Marker }).ToArray();
            bool complete = false;
            try
            {
                string[] resources = { "payload.app", "payload.help", "payload.uninstall", "payload.icon" };
                for (int i = 0; i < resources.Length; i++)
                    using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resources[i]))
                    {
                        if (input == null) throw new IOException("安装包中的文件缺失，请重新获取完整安装包。");
                        using (var output = File.Create(Path.Combine(stage, names[i]))) input.CopyTo(output);
                    }
                File.WriteAllText(Path.Combine(stage, InstallCommon.Marker), InstallCommon.Product + "\r\nVersion=1.3.2\r\n", new UTF8Encoding(false));
                foreach (string name in names)
                {
                    string destination = Path.Combine(directory, name), backup = Path.Combine(stage, name + ".previous");
                    if (File.Exists(destination)) { File.Move(destination, backup); backups.Add(name); }
                    File.Move(Path.Combine(stage, name), destination); committed.Add(name);
                }
                complete = true;
            }
            catch
            {
                foreach (string name in committed.AsEnumerable().Reverse())
                    try { File.Delete(Path.Combine(directory, name)); } catch { }
                foreach (string name in backups)
                    try { File.Move(Path.Combine(stage, name + ".previous"), Path.Combine(directory, name)); } catch { }
                throw;
            }
            finally
            {
                foreach (string name in names)
                {
                    try { File.Delete(Path.Combine(stage, name)); } catch { }
                    // Preserve a backup if rollback could not restore it.
                    if (complete) try { File.Delete(Path.Combine(stage, name + ".previous")); } catch { }
                }
                try { Directory.Delete(stage, false); } catch { }
            }
            string result = "安装完成：" + directory + "\r\n版本：1.3.2。原有 Data 文本与设置已保留。";
            if (desktopShortcut)
                try { InstallCommon.CreateShortcut(directory); result += "\r\n已创建桌面快捷方式。"; }
                catch (Exception ex) { result += "\r\n程序已安装，但快捷方式未创建：" + ex.Message; }
            return result;
        }
        [STAThread] static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--install")
            {
                string log = null; bool desktop = false;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--desktop") desktop = true;
                    else if (args[i] == "--log" && i + 1 < args.Length) log = args[++i];
                    else return 2;
                }
                try { string message = InstallTo(args[1], desktop); if (log != null) File.WriteAllText(log, message, Encoding.UTF8); return 0; }
                catch (Exception ex) { if (log != null) File.WriteAllText(log, "安装失败：" + ex.Message, Encoding.UTF8); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm()); return 0;
        }
    }

    internal sealed class SetupForm : Form
    {
        readonly TextBox directory = new TextBox();
        readonly CheckBox shortcut = new CheckBox(), launch = new CheckBox();
        readonly Button install = new Button();
        readonly Label result = new Label();
        readonly bool renderOnly;
        bool installed;
        protected override bool ShowWithoutActivation { get { return renderOnly; } }
        internal SetupForm(bool renderOnly = false)
        {
            this.renderOnly = renderOnly; Icon = SequencePasteBranding.AppIcon.Load();
            Text = "顺序贴 1.3.2 · 安装向导"; Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(620, 430); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(247, 249, 253);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 8 };
            foreach (float h in new[] { 54F, 55F, 26F, 40F, 34F, 34F, 68F, 52F }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "安装顺序贴", Font = new Font(Font.FontFamily, 23F, FontStyle.Bold), AutoSize = true }, 0, 0);
            root.Controls.Add(new Label { Text = "自定义复制、粘贴快捷键 · 顺序 / 批量粘贴\r\n置顶迷你窗口 · 编辑和调整文本队列", AutoSize = true, ForeColor = Color.FromArgb(83, 100, 123) }, 0, 1);
            root.Controls.Add(new Label { Text = "安装目录", AutoSize = true }, 0, 2);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            directory.Dock = DockStyle.Fill; directory.Text = Directory.Exists("J:\\") ? "J:\\顺序贴" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "顺序贴");
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill }; pathRow.Controls.Add(directory, 0, 0); pathRow.Controls.Add(browse, 1, 0); root.Controls.Add(pathRow, 0, 3);
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择空文件夹，或已有顺序贴安装目录", SelectedPath = directory.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath; };
            shortcut.Text = "创建桌面快捷方式"; shortcut.Checked = true; shortcut.AutoSize = true; root.Controls.Add(shortcut, 0, 4);
            launch.Text = "安装完成后启动顺序贴"; launch.Checked = true; launch.AutoSize = true; root.Controls.Add(launch, 0, 5);
            result.Text = "更新安装会保留 Data 中的文本与设置。\r\n安装前请先退出旧版顺序贴。"; result.Dock = DockStyle.Fill; result.AutoEllipsis = true; root.Controls.Add(result, 0, 6);
            install.Text = "立即安装"; install.Size = new Size(145, 40); install.Anchor = AnchorStyles.Right; install.BackColor = Color.FromArgb(35, 99, 220); install.ForeColor = Color.White; install.FlatStyle = FlatStyle.Flat;
            install.Click += delegate
            {
                if (installed) { Close(); return; }
                install.Enabled = false;
                try
                {
                    result.Text = Installer.InstallTo(directory.Text, shortcut.Checked); installed = true;
                    directory.Enabled = browse.Enabled = shortcut.Enabled = launch.Enabled = false; install.Text = "完成";
                    if (launch.Checked)
                        try { Process.Start(new ProcessStartInfo(Path.Combine(InstallCommon.FullDirectory(directory.Text), InstallCommon.AppName)) { UseShellExecute = true }); }
                        catch (Exception ex) { result.Text += "\r\n请手动启动：" + ex.Message; }
                }
                catch (Exception ex) { result.Text = "安装失败：" + ex.Message; result.ForeColor = Color.FromArgb(173, 55, 42); }
                finally { install.Enabled = true; }
            };
            root.Controls.Add(install, 0, 7);
        }
    }
}
