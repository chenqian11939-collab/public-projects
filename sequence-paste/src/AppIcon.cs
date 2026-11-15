using System;
using System.Drawing;
using System.Reflection;

namespace SequencePasteBranding
{
    internal static class AppIcon
    {
        internal static Icon Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SequencePaste.AppIcon"))
            {
                if (stream == null) throw new InvalidOperationException("程序图标资源缺失，请重新安装顺序贴。");
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
}
