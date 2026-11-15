using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("卸载顺序贴")]
[assembly: System.Reflection.AssemblyVersion("1.3.2.0")]

namespace SequencePasteSetup
{
    internal static class Uninstall
    {
        internal static void Remove(string requested)
        {
            string directory = InstallCommon.FullDirectory(requested);
            if (!InstallCommon.IsInstalled(directory)) throw new IOException("没有找到有效的顺序贴安装标记，未删除任何文件。");
            if (String.Equals(directory, AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("请从安装目录双击卸载程序启动卸载。");
            InstallCommon.EnsureNotRunning(directory);
            string target = Path.Combine(directory, InstallCommon.AppName);
            if (InstallCommon.ShortcutPointsTo(InstallCommon.DesktopLink, target)) File.Delete(InstallCommon.DesktopLink);
            foreach (string name in InstallCommon.Files) File.Delete(Path.Combine(directory, name));
            File.Delete(Path.Combine(directory, InstallCommon.Marker));
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
        }
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            if (args.Length >= 2 && args[0] == "--remove")
            {
                bool quiet = args.Contains("--quiet");
                try
                {
                    if (!quiet && MessageBox.Show("卸载顺序贴？\r\n程序和本次安装的桌面快捷方式会移除。\r\nData 文件夹中的文本与设置会保留。", "卸载顺序贴", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
                    Remove(args[1]); if (!quiet) MessageBox.Show("已卸载。Data 文本与设置已保留。", "顺序贴"); return 0;
                }
                catch (Exception ex) { if (!quiet) MessageBox.Show(ex.Message, "卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
            }
            try
            {
                string directory = InstallCommon.FullDirectory(AppDomain.CurrentDomain.BaseDirectory);
                if (!InstallCommon.IsInstalled(directory)) throw new IOException("请在顺序贴的安装目录运行卸载程序。");
                string temporary = Path.Combine(Path.GetTempPath(), "SequencePaste-Uninstall-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary); string helper = Path.Combine(temporary, InstallCommon.UninstallName);
                File.Copy(Application.ExecutablePath, helper);
                Process.Start(new ProcessStartInfo(helper, "--remove " + InstallCommon.Quote(directory)) { UseShellExecute = true }); return 0;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        }
    }
}
