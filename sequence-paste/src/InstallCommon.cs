using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
namespace SequencePasteSetup
{
    internal static class InstallCommon
    {
        internal const string Product = "SequencePaste.Install.v1";
        internal const string Marker = "install.info";
        internal const string AppName = "顺序贴.exe";
        internal const string UninstallName = "卸载顺序贴.exe";
        internal const string IconName = "顺序贴.ico";
        internal static readonly string[] Files = { AppName, "使用说明.txt", UninstallName, IconName };
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
        internal static string FullDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new IOException("请选择完整的安装目录。");
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar);
            if (String.Equals(full, root, StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择磁盘下的独立文件夹，不要直接安装到磁盘根目录。");
            return full;
        }
        internal static bool IsInstalled(string directory)
        {
            string marker = Path.Combine(directory, Marker);
            return File.Exists(marker) && File.ReadAllText(marker).StartsWith(Product + "\r\n", StringComparison.Ordinal);
        }
        internal static void EnsureNotRunning(string directory)
        {
            foreach (string name in new[] { AppName, UninstallName })
            {
                string file = Path.Combine(directory, name);
                if (File.Exists(file))
                    try { using (var stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException) { throw new IOException("请先从托盘退出该目录中正在运行的顺序贴，再重试。"); }
            }
        }
        internal static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "顺序贴.lnk"); } }
        internal static object Get(object instance, string name) { return instance.GetType().InvokeMember(name, BindingFlags.GetProperty, null, instance, null); }
        internal static void Set(object instance, string name, object value) { instance.GetType().InvokeMember(name, BindingFlags.SetProperty, null, instance, new[] { value }); }
        internal static bool ShortcutPointsTo(string linkPath, string target)
        {
            if (!File.Exists(linkPath)) return false;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                return String.Equals(Convert.ToString(Get(shortcut, "TargetPath")), target, StringComparison.OrdinalIgnoreCase);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        internal static void CreateShortcut(string directory)
        {
            string target = Path.Combine(directory, AppName), link = DesktopLink;
            if (File.Exists(link) && !ShortcutPointsTo(link, target)) throw new IOException("桌面上已有另一个位置的同名快捷方式，已保留原快捷方式。");
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                Set(shortcut, "TargetPath", target); Set(shortcut, "WorkingDirectory", directory);
                Set(shortcut, "Description", "顺序贴：自定义复制、顺序粘贴和迷你窗口"); Set(shortcut, "IconLocation", Path.Combine(directory, IconName) + ",0");
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                SHChangeNotify(0x00002000, 0x0005, link, IntPtr.Zero);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        internal static string Quote(string value) { return "\"" + value + "\""; }
    }
}
