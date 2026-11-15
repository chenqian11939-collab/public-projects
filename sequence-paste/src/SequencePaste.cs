using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("顺序贴")]
[assembly: System.Reflection.AssemblyProduct("顺序贴")]
[assembly: System.Reflection.AssemblyVersion("1.3.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.2.0")]

namespace SequencePaste
{
    [DataContract]
    public sealed class Shortcut
    {
        [DataMember] public int Modifiers;
        [DataMember] public int Key;
        public Shortcut() { }
        public Shortcut(int modifiers, Keys key) { Modifiers = modifiers; Key = (int)key; }
        public Shortcut Copy() { return new Shortcut(Modifiers, (Keys)Key); }
        public static readonly string[] ActionNames = { "复制选中文本", "粘贴当前剪贴板", "顺序粘贴下一条", "批量开始 / 停止" };
        public static bool Same(Shortcut[] left, Shortcut[] right)
        {
            return left != null && right != null && left.Length == right.Length &&
                left.Select((s, i) => s != null && right[i] != null && s.Key == right[i].Key && s.Modifiers == right[i].Modifiers).All(x => x);
        }
        public override string ToString()
        {
            return ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") +
                ((Modifiers & 4) != 0 ? "Shift + " : "") + KeyName((Keys)Key);
        }
        public static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
            if (key == Keys.Space) return "Space";
            if (key == Keys.PageUp) return "PageUp";
            if (key == Keys.PageDown) return "PageDown";
            return key.ToString();
        }
        public static List<Keys> AllowedKeys()
        {
            var result = new List<Keys>();
            for (int k = (int)Keys.A; k <= (int)Keys.Z; k++) result.Add((Keys)k);
            for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++) result.Add((Keys)k);
            for (int k = (int)Keys.F1; k <= (int)Keys.F24; k++) if (k != (int)Keys.F12) result.Add((Keys)k);
            result.AddRange(new[] { Keys.Space, Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Up, Keys.Down, Keys.Left, Keys.Right });
            return result;
        }
        public static string Validate(Shortcut[] values)
        {
            if (values == null || values.Length != 4) return "需要设置四个快捷键。";
            var seen = new HashSet<string>();
            for (int i = 0; i < values.Length; i++)
            {
                var s = values[i]; string label = "“" + ActionNames[i] + "”：";
                if (s == null || s.Modifiers < 0 || s.Modifiers > 7 || !AllowedKeys().Contains((Keys)s.Key)) return label + "请选择支持的按键。";
                bool function = s.Key >= (int)Keys.F1 && s.Key <= (int)Keys.F24;
                if ((s.Modifiers & 3) == 0 && !function) return label + s + " 不能单独使用，请勾选 Ctrl 或 Alt，或改用 F1、F2 等功能键。";
                if (s.Modifiers == 2 && (s.Key == (int)Keys.C || s.Key == (int)Keys.V)) return label + "请保留系统 Ctrl+C / Ctrl+V，选择另一组快捷键。";
                if (((s.Modifiers & 3) == 3 && s.Key == (int)Keys.Delete) || ((s.Modifiers & 1) != 0 && s.Key == (int)Keys.F4)) return label + "这个组合是系统保留快捷键，请换一个。";
                if (!seen.Add(s.Modifiers + ":" + s.Key)) return label + s + " 与其他功能重复，请换一个。";
            }
            return null;
        }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<string> Items = new List<string>();
        [DataMember] public int NextIndex;
        [DataMember] public bool CaptureCopies = true;
        [DataMember] public bool LoopSingle;
        [DataMember] public int IntervalMs = 500;
        [DataMember] public int Separator = 1;
        [DataMember] public int BatchLimit;
        [DataMember] public bool HotkeysDisabled;
        [DataMember] public bool MiniHidden;
        [DataMember] public bool MiniUnpinned;
        [DataMember] public bool MiniBoundsSaved;
        [DataMember] public int MiniX, MiniY, MiniWidth, MiniHeight;
        [DataMember] public Shortcut[] Hotkeys = Defaults();
        public static Shortcut[] Defaults() { return new[] { new Shortcut(0, Keys.F1), new Shortcut(0, Keys.F4), new Shortcut(0, Keys.F2), new Shortcut(0, Keys.F3) }; }
        public void Normalize()
        {
            if (Items == null) Items = new List<string>();
            Items = Items.Where(x => !String.IsNullOrEmpty(x)).ToList();
            NextIndex = Math.Max(0, Math.Min(NextIndex, Items.Count));
            IntervalMs = Math.Max(150, Math.Min(10000, IntervalMs));
            Separator = Math.Max(0, Math.Min(2, Separator));
            BatchLimit = Math.Max(0, Math.Min(100000, BatchLimit));
            if (Shortcut.Validate(Hotkeys) != null) Hotkeys = Defaults();
        }
    }

    public static class QueueRules
    {
        public static int Start(int next, int count, bool loop)
        {
            if (count == 0) return -1;
            if (next >= count) return loop ? 0 : -1;
            return Math.Max(0, next);
        }
        public static int End(int start, int count, int limit) { return limit <= 0 ? count : start + Math.Min(limit, count - start); }
        public static string[] Lines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(x => x.Length > 0).ToArray();
        }
        public static int Move(AppState state, int from, int delta)
        {
            int to = from + delta;
            if (from < 0 || from >= state.Items.Count || to < 0 || to >= state.Items.Count) return from;
            string value = state.Items[from]; state.Items.RemoveAt(from); state.Items.Insert(to, value);
            if (state.NextIndex == from) state.NextIndex = to;
            else if (from < state.NextIndex && state.NextIndex <= to) state.NextIndex--;
            else if (to <= state.NextIndex && state.NextIndex < from) state.NextIndex++;
            return to;
        }
        public static bool Remove(AppState state, int index)
        {
            if (index < 0 || index >= state.Items.Count) return false;
            state.Items.RemoveAt(index);
            if (index < state.NextIndex) state.NextIndex--;
            state.NextIndex = Math.Min(state.NextIndex, state.Items.Count);
            return true;
        }
        public static int DropDestination(int from, int gap, int count)
        {
            if (from < 0 || from >= count || gap < 0 || gap > count) return from;
            return gap > from ? gap - 1 : gap;
        }
        public static string Progress(AppState state)
        {
            int next = Start(state.NextIndex, state.Items.Count, state.LoopSingle);
            if (state.Items.Count == 0) return "队列为空";
            if (next < 0) return "已到末尾 · 共 " + state.Items.Count + " 条";
            return "下次：第 " + (next + 1) + " 条 / 共 " + state.Items.Count + " 条" + (state.NextIndex >= state.Items.Count ? "（循环）" : "");
        }
    }

    public static class StateStore
    {
        public static AppState Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var value = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
                if (value == null) throw new InvalidDataException("设置文件为空。");
                value.Normalize(); return value;
            }
        }
        public static void Write(string path, AppState value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, value);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }

    internal static class Native
    {
        public const int WM_HOTKEY = 0x312;
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct HARDWAREINPUT { public uint msg; public ushort low, high; }
        private static INPUT Key(Keys key, bool up)
        {
            return new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { vk = (ushort)key, flags = up ? 2u : 0u } } };
        }
        public static bool Press(Keys key, bool control)
        {
            var keys = control ? new[] { Key(Keys.ControlKey, false), Key(key, false), Key(key, true), Key(Keys.ControlKey, true) } : new[] { Key(key, false), Key(key, true) };
            uint written = SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT)));
            if (written == keys.Length) return true;
            // If input was partially accepted, release only keys used by this operation.
            var releases = control ? new[] { Key(key, true), Key(Keys.ControlKey, true) } : new[] { Key(key, true) };
            SendInput((uint)releases.Length, releases, Marshal.SizeOf(typeof(INPUT)));
            return false;
        }
        public static bool Down(Keys key) { return (GetAsyncKeyState((int)key) & 0x8000) != 0; }
        public static bool ModifiersDown() { return Down(Keys.ControlKey) || Down(Keys.Menu) || Down(Keys.ShiftKey) || Down(Keys.LWin) || Down(Keys.RWin); }
    }

    // The desktop boundary is injectable so timing checks do not send keys to a user's applications.
    internal interface IClipboardDesktop
    {
        IntPtr ForegroundWindow { get; }
        DateTime UtcNow { get; }
        uint ClipboardSequence { get; }
        bool IsOwnWindow(IntPtr window);
        bool ModifiersDown();
        bool Down(Keys key);
        bool Press(Keys key, bool control);
        string ReadClipboardText();
        void SetClipboardText(string text);
    }
    internal sealed class WindowsClipboardDesktop : IClipboardDesktop
    {
        public IntPtr ForegroundWindow { get { return Native.GetForegroundWindow(); } }
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public uint ClipboardSequence { get { return Native.GetClipboardSequenceNumber(); } }
        public bool IsOwnWindow(IntPtr window) { uint pid; Native.GetWindowThreadProcessId(window, out pid); return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id; }
        public bool ModifiersDown() { return Native.ModifiersDown(); }
        public bool Down(Keys key) { return Native.Down(key); }
        public bool Press(Keys key, bool control) { return Native.Press(key, control); }
        public string ReadClipboardText() { return Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null; }
        public void SetClipboardText(string text) { Clipboard.SetDataObject(text, true, 5, 20); }
    }

    internal sealed class ShortcutEditor : FlowLayoutPanel
    {
        readonly CheckBox ctrl = new CheckBox { Text = "Ctrl", AutoSize = true };
        readonly CheckBox alt = new CheckBox { Text = "Alt", AutoSize = true };
        readonly CheckBox shift = new CheckBox { Text = "Shift", AutoSize = true };
        readonly ComboBox key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
        readonly List<Keys> keys = Shortcut.AllowedKeys();
        public event EventHandler Changed;
        public ShortcutEditor(Shortcut s)
        {
            Dock = DockStyle.Fill; WrapContents = false; AutoSize = true; Padding = new Padding(0, 4, 0, 0);
            ctrl.Margin = alt.Margin = shift.Margin = new Padding(4, 5, 12, 0);
            key.Items.AddRange(keys.Select(x => Shortcut.KeyName(x)).ToArray());
            Controls.AddRange(new Control[] { ctrl, alt, shift, key }); Set(s);
            ctrl.CheckedChanged += OnChanged; alt.CheckedChanged += OnChanged; shift.CheckedChanged += OnChanged; key.SelectedIndexChanged += OnChanged;
        }
        void OnChanged(object sender, EventArgs e) { if (Changed != null) Changed(this, EventArgs.Empty); }
        public void Set(Shortcut s) { ctrl.Checked = (s.Modifiers & 2) != 0; alt.Checked = (s.Modifiers & 1) != 0; shift.Checked = (s.Modifiers & 4) != 0; key.SelectedIndex = keys.IndexOf((Keys)s.Key); }
        public Shortcut Get() { return new Shortcut((ctrl.Checked ? 2 : 0) | (alt.Checked ? 1 : 0) | (shift.Checked ? 4 : 0), key.SelectedIndex < 0 ? Keys.None : keys[key.SelectedIndex]); }
    }

    public sealed class MainForm : Form
    {
        const int HotkeyBase = 4000, EscapeId = 4010;
        static readonly Color Ink = Color.FromArgb(30, 42, 58), Blue = Color.FromArgb(35, 99, 220), Muted = Color.FromArgb(91, 107, 129);
        readonly string statePath;
        readonly IClipboardDesktop desktop;
        AppState model;
        readonly Label status = new Label(), progress = new Label(), shortcutSummary = new Label();
        readonly ListBox queue = new ListBox();
        readonly TextBox editor = new TextBox();
        readonly CheckBox capture = new CheckBox(), loop = new CheckBox();
        readonly ComboBox separator = new ComboBox();
        readonly NumericUpDown interval = new NumericUpDown(), batchLimit = new NumericUpDown();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly List<Control> mutationControls = new List<Control>();
        readonly ShortcutEditor[] shortcutEditors = new ShortcutEditor[4];
        readonly Button stop = new Button();
        readonly Label hotkeyState = new Label();
        readonly Label hotkeyDraft = new Label();
        Button enableHotkeys, disableHotkeys;
        ToolStripMenuItem trayHotkeyToggle;
        MiniQueueForm miniWindow;
        bool ready, closing, exitRequested, registered, escapeRegistered, saveProblem, refreshingQueue, queueDialogOpen;
        string startupWarning = "";
        int action = -1, stage, batchPosition, batchEnd, pastedCount;
        string[] snapshot;
        DateTime due, releaseDeadline;
        IntPtr target;
        uint oldClipboard;
        Keys trigger;

        public MainForm(string dataDirectory) : this(dataDirectory, new WindowsClipboardDesktop()) { }
        internal MainForm(string dataDirectory, IClipboardDesktop desktop)
        {
            this.desktop = desktop;
            statePath = Path.Combine(dataDirectory, "settings.json");
            model = LoadState();
            Text = "顺序贴 · 本地复制粘贴工具";
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = Ink; BackColor = Color.FromArgb(243, 246, 251);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1080, 720); MinimumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SequencePasteBranding.AppIcon.Load();
            BuildUi(); BuildTray();
            timer.Interval = 10; timer.Tick += Tick;
            saveTimer.Interval = 250; saveTimer.Tick += delegate { Save(); };
            Shown += delegate
            {
                ready = true;
                if (!model.HotkeysDisabled) registered = RegisterAll(model.Hotkeys, out startupRegistrationError);
                RefreshQueue(); ShowShortcutSummary();
                SetStatus(startupWarning.Length > 0 ? startupWarning : registered ? "热键已启用。切换到目标程序后，按设置的快捷键复制或粘贴。" : model.HotkeysDisabled ? "热键已关闭，按键恢复正常用途。点击右上角“启用热键”开始使用。" : startupRegistrationError, startupWarning.Length > 0 || (!registered && !model.HotkeysDisabled));
                if (!model.MiniHidden) ShowMiniWindow(false);
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); tray.ShowBalloonTip(1800, "顺序贴正在运行", registered ? "热键已启用。双击托盘图标打开窗口。" : "热键已关闭，按键保持正常用途。双击托盘图标打开窗口。", ToolTipIcon.Info); } };
        }
        string startupRegistrationError;
        AppState LoadState()
        {
            if (!File.Exists(statePath)) return new AppState();
            try { return StateStore.Read(statePath); }
            catch (Exception ex)
            {
                try
                {
                    var recovered = StateStore.Read(statePath + ".bak");
                    File.Move(statePath, statePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
                    startupWarning = "设置文件损坏，已保留原文件并恢复上一次备份。"; return recovered;
                }
                catch
                {
                    try { File.Move(statePath, statePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")); }
                    catch { }
                    startupWarning = "未能读取旧设置：" + ex.Message + "。请检查 Data 文件夹。";
                    return new AppState();
                }
            }
        }
        static Label LabelText(string text, bool strong)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = strong ? Ink : Muted, Font = new Font("Microsoft YaHei UI", strong ? 10F : 9F, strong ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 5, 0, 7) };
        }
        Button ButtonText(string text, EventHandler click, bool primary)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(78, 34), Padding = new Padding(10, 2, 10, 2), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 6) };
            b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(211, 220, 232);
            b.Click += click; return b;
        }
        FlowLayoutPanel Flow() { return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) }; }
        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 12), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            Controls.Add(root);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "顺序贴", Font = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            header.Controls.Add(new Label { Text = "自定义快捷键  /  按顺序粘贴  /  完全本地运行", ForeColor = Muted, AutoSize = true, Location = new Point(170, 20) });
            var hotkeyBar = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 330, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
            hotkeyState.AutoSize = false; hotkeyState.Size = new Size(100, 36); hotkeyState.TextAlign = ContentAlignment.MiddleCenter; hotkeyState.Margin = new Padding(0, 0, 8, 0);
            hotkeyState.Font = new Font(Font, FontStyle.Bold); hotkeyBar.Controls.Add(hotkeyState);
            enableHotkeys = ButtonText("启用热键", delegate { SetHotkeysEnabled(true); }, true);
            disableHotkeys = ButtonText("关闭热键", delegate { SetHotkeysEnabled(false); }, false);
            hotkeyBar.Controls.Add(enableHotkeys); hotkeyBar.Controls.Add(disableHotkeys); header.Controls.Add(hotkeyBar);
            root.Controls.Add(header, 0, 0);
            shortcutSummary.Dock = DockStyle.Fill; shortcutSummary.BackColor = Color.FromArgb(230, 238, 254); shortcutSummary.ForeColor = Color.FromArgb(31, 70, 146); shortcutSummary.Padding = new Padding(12, 10, 4, 0); root.Controls.Add(shortcutSummary, 0, 1);
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8), Margin = new Padding(0, 14, 0, 0) };
            var itemsTab = new TabPage("文本与顺序") { BackColor = Color.White, Padding = new Padding(14) };
            var hotkeyTab = new TabPage("快捷键设置") { BackColor = Color.White, Padding = new Padding(22), AutoScroll = true };
            tabs.TabPages.Add(itemsTab); tabs.TabPages.Add(hotkeyTab); root.Controls.Add(tabs, 0, 2);
            BuildItems(itemsTab); BuildHotkeys(hotkeyTab);
            var bottom = Flow(); bottom.Padding = new Padding(0, 10, 0, 0);
            progress.AutoSize = true; progress.Font = new Font(Font, FontStyle.Bold); progress.Margin = new Padding(0, 7, 20, 0); bottom.Controls.Add(progress);
            var reset = ButtonText("从第 1 条开始", delegate { model.NextIndex = 0; RefreshQueue(false); Save(); SetStatus("顺序已重置，下次从第 1 条开始。", false); }, false);
            bottom.Controls.Add(reset); mutationControls.Add(reset);
            stop.Text = "停止批量 · Esc"; stop.AutoSize = true; stop.Height = 32; stop.Enabled = false; stop.Click += delegate { Cancel("已停止。下次从未粘贴的条目继续。"); }; bottom.Controls.Add(stop);
            bottom.Controls.Add(ButtonText("最小化到托盘", delegate { WindowState = FormWindowState.Minimized; }, false)); root.Controls.Add(bottom, 0, 3);
            bottom.Controls.Add(ButtonText("迷你窗口", delegate { ShowMiniWindow(true); }, true));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; root.Controls.Add(status, 0, 4);
        }
        void BuildItems(Control parent)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104)); parent.Controls.Add(layout);
            layout.Controls.Add(LabelText("01   文本队列", true), 0, 0); layout.Controls.Add(LabelText("02   添加或编辑文本", true), 1, 0);
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 18, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); layout.Controls.Add(left, 0, 1);
            queue.Dock = DockStyle.Fill; queue.IntegralHeight = false; queue.DrawMode = DrawMode.OwnerDrawFixed; queue.ItemHeight = 32;
            queue.BorderStyle = BorderStyle.FixedSingle; queue.DrawItem += DrawQueueItem; queue.SelectedIndexChanged += delegate { if (!refreshingQueue && queue.SelectedIndex >= 0 && queue.SelectedIndex < model.Items.Count) editor.Text = model.Items[queue.SelectedIndex]; };
            left.Controls.Add(queue, 0, 0);
            var queueButtons = Flow(); queueButtons.AutoSize = false; queueButtons.Padding = new Padding(0, 8, 0, 0);
            queueButtons.Controls.Add(ButtonText("设为下一条", delegate { SetNextQueueItem(queue.SelectedIndex); }, false));
            queueButtons.Controls.Add(ButtonText("上移", delegate { MoveItem(-1); }, false)); queueButtons.Controls.Add(ButtonText("下移", delegate { MoveItem(1); }, false));
            queueButtons.Controls.Add(ButtonText("删除所选", delegate { DeleteSelected(); }, false)); queueButtons.Controls.Add(ButtonText("清空", delegate { ClearItems(); }, false));
            left.Controls.Add(queueButtons, 0, 1); mutationControls.Add(queueButtons);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.Controls.Add(right, 1, 1);
            editor.Multiline = true; editor.AcceptsReturn = true; editor.AcceptsTab = true; editor.ScrollBars = ScrollBars.Vertical; editor.Dock = DockStyle.Fill; editor.BorderStyle = BorderStyle.FixedSingle; editor.Font = new Font("Microsoft YaHei UI", 10F); editor.AccessibleName = "文本编辑区"; right.Controls.Add(editor, 0, 0); mutationControls.Add(editor);
            var editButtons = Flow(); editButtons.Padding = new Padding(0, 8, 0, 0);
            editButtons.Controls.Add(ButtonText("添加整段", delegate { AddItems(new[] { editor.Text }); }, true));
            editButtons.Controls.Add(ButtonText("逐行添加", delegate { AddItems(QueueRules.Lines(editor.Text)); }, false));
            editButtons.Controls.Add(ButtonText("更新所选", delegate { UpdateSelected(); }, false)); right.Controls.Add(editButtons, 0, 1); mutationControls.Add(editButtons);
            var fileButtons = Flow();
            fileButtons.Controls.Add(ButtonText("导入 TXT", ImportText, false)); fileButtons.Controls.Add(ButtonText("导出队列", ExportQueue, false)); fileButtons.Controls.Add(ButtonText("导入队列", ImportQueue, false)); right.Controls.Add(fileButtons, 0, 2); mutationControls.Add(fileButtons);
            right.Controls.Add(LabelText("整段可含换行；逐行添加会跳过空行。", false), 0, 3);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 8, 0, 0) }; layout.Controls.Add(options, 0, 2); layout.SetColumnSpan(options, 2);
            var flags = Flow(); capture.Text = "复制时自动加入队列"; capture.AutoSize = true; capture.Checked = model.CaptureCopies; capture.Margin = new Padding(0, 3, 25, 0); loop.Text = "单条粘贴到末尾后循环"; loop.AutoSize = true; loop.Checked = model.LoopSingle; flags.Controls.Add(capture); flags.Controls.Add(loop); options.Controls.Add(flags, 0, 0);
            var settings = Flow(); separator.DropDownStyle = ComboBoxStyle.DropDownList; separator.Items.AddRange(new object[] { "无分隔", "Tab（下一个输入框）", "回车（换行 / 提交）" }); separator.SelectedIndex = model.Separator; separator.Width = 180;
            interval.Minimum = 150; interval.Maximum = 10000; interval.Increment = 50; interval.Value = model.IntervalMs; interval.Width = 83;
            batchLimit.Minimum = 0; batchLimit.Maximum = 100000; batchLimit.Value = model.BatchLimit; batchLimit.Width = 75;
            settings.Controls.Add(LabelText("批量条目之间", false)); settings.Controls.Add(separator); settings.Controls.Add(LabelText("  间隔（毫秒）", false)); settings.Controls.Add(interval); settings.Controls.Add(LabelText("  每批条数", false)); settings.Controls.Add(batchLimit); settings.Controls.Add(LabelText("0 = 剩余全部", false)); options.Controls.Add(settings, 0, 1);
            options.Controls.Add(LabelText("设置和文本自动保存在程序目录 Data；顺序粘贴会更新系统剪贴板。", false), 0, 2); mutationControls.Add(options);
            capture.CheckedChanged += OptionsChanged; loop.CheckedChanged += OptionsChanged; separator.SelectedIndexChanged += OptionsChanged; interval.ValueChanged += OptionsChanged; batchLimit.ValueChanged += OptionsChanged;
        }
        void BuildHotkeys(Control parent)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); parent.Controls.Add(layout);
            var intro = LabelText("选好按键后点击“启用热键”，会同时保存并应用；关闭后恢复正常用途。", true); layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 2);
            string[] labels = Shortcut.ActionNames;
            for (int i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 0 ? 48 : 52));
                layout.Controls.Add(LabelText(labels[i], true), 0, i + 1); shortcutEditors[i] = new ShortcutEditor(model.Hotkeys[i]); layout.Controls.Add(shortcutEditors[i], 1, i + 1);
                shortcutEditors[i].Changed += delegate { if (ready) ShowShortcutSummary(); };
            }
            var actions = Flow(); actions.Padding = new Padding(0, 16, 0, 10); actions.Controls.Add(ButtonText("应用快捷键", ApplyShortcuts, true));
            actions.Controls.Add(ButtonText("恢复默认组合", delegate { var defaults = AppState.Defaults(); for (int i = 0; i < 4; i++) shortcutEditors[i].Set(defaults[i]); SetStatus("默认组合已填入；点击“应用快捷键”生效。", false); }, false));
            actions.Controls.Add(ButtonText("填入 F1–F4 方案", delegate { var keys = new[] { Keys.F1, Keys.F4, Keys.F2, Keys.F3 }; for (int i = 0; i < 4; i++) shortcutEditors[i].Set(new Shortcut(0, keys[i])); SetStatus("已填入 F1 复制、F2 顺序粘贴、F3 批量、F4 当前剪贴板；点击“启用热键”生效。", false); }, false));
            layout.Controls.Add(actions, 0, 5); layout.SetColumnSpan(actions, 2);
            hotkeyDraft.AutoSize = true; hotkeyDraft.MaximumSize = new Size(870, 0); hotkeyDraft.Margin = new Padding(0, 3, 0, 4);
            layout.Controls.Add(hotkeyDraft, 0, 6); layout.SetColumnSpan(hotkeyDraft, 2);
            var help = new Label { AutoSize = true, MaximumSize = new Size(870, 0), ForeColor = Muted, Text = "快捷键被其他软件占用时，会显示具体冲突，不会悄悄失效。\r\n\r\n批量操作从“下一条”开始，到队列末尾或本批条数后停止。按 Esc 或再次按批量快捷键停止。\r\n切换到其他窗口也会自动停止。Ctrl+C / Ctrl+V 保留给系统使用。\r\n\r\n本工具只处理文本。目标程序需支持 Ctrl+C / Ctrl+V。若目标程序以管理员身份运行，\r\n本工具也需以相同权限运行。回车在聊天或网页中可能触发发送/提交，请先试用。", Margin = new Padding(0, 12, 0, 0) };
            layout.Controls.Add(help, 0, 7); layout.SetColumnSpan(help, 2); mutationControls.Add(layout);
        }
        void BuildTray()
        {
            tray.Icon = Icon; tray.Text = "顺序贴 · 本地复制粘贴工具"; tray.Visible = true;
            var menu = new ContextMenuStrip(); menu.Items.Add("打开主窗口", null, delegate { ShowMain(); });
            menu.Items.Add("显示迷你窗口", null, delegate { ShowMiniWindow(true); });
            trayHotkeyToggle = new ToolStripMenuItem("启用热键");
            trayHotkeyToggle.Click += delegate { SetHotkeysEnabled(!registered); };
            menu.Items.Add(trayHotkeyToggle); menu.Items.Add("退出", null, delegate { exitRequested = true; Close(); }); tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowMain(); };
        }
        internal void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        internal void ToggleMainWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized) Hide(); else ShowMain();
        }
        internal void ToggleHotkeys() { SetHotkeysEnabled(!registered); }
        internal void ShowMiniWindow(bool activate)
        {
            if (miniWindow == null || miniWindow.IsDisposed) miniWindow = new MiniQueueForm(this, model);
            model.MiniHidden = false; RefreshMiniWindow();
            if (!miniWindow.Visible) miniWindow.Show();
            if (activate) miniWindow.Activate();
            Save();
        }
        internal void MiniWindowHidden() { model.MiniHidden = true; if (!closing) Save(); }
        internal void SetMiniPinned(bool pinned) { model.MiniUnpinned = !pinned; if (!closing) Save(); }
        internal void SaveMiniBounds(Rectangle bounds)
        {
            model.MiniX = bounds.X; model.MiniY = bounds.Y; model.MiniWidth = bounds.Width; model.MiniHeight = bounds.Height;
            model.MiniBoundsSaved = true; if (!closing) Save();
        }
        void RefreshMiniWindow()
        {
            if (miniWindow != null && !miniWindow.IsDisposed) miniWindow.RefreshFrom(model, registered, action >= 0);
        }
        void ShowShortcutSummary()
        {
            var pending = shortcutEditors.Select(x => x.Get()).ToArray();
            bool changed = !Shortcut.Same(pending, model.Hotkeys);
            string validation = Shortcut.Validate(pending);
            hotkeyDraft.Text = validation != null ? "尚未应用：" + validation : changed ? "按键已修改，尚未应用。点击“应用快捷键”或右上角“应用并启用”。" : registered ? "以上按键已生效。" : "以上按键已保存，当前热键关闭。";
            hotkeyDraft.ForeColor = validation != null ? Color.FromArgb(173, 55, 42) : changed ? Color.FromArgb(159, 100, 15) : Muted;
            string prefix = registered ? "" : "热键已关闭   ";
            shortcutSummary.Text = prefix + "复制 " + model.Hotkeys[0] + "     粘贴 " + model.Hotkeys[1] + "     下一条 " + model.Hotkeys[2] + "     批量 " + model.Hotkeys[3];
            shortcutSummary.BackColor = registered ? Color.FromArgb(230, 238, 254) : Color.FromArgb(233, 237, 243);
            shortcutSummary.ForeColor = registered ? Color.FromArgb(31, 70, 146) : Muted;
            hotkeyState.Text = registered ? "● 热键已启用" : "○ 热键已关闭";
            hotkeyState.ForeColor = registered ? Color.FromArgb(25, 123, 77) : Muted;
            enableHotkeys.Text = changed ? "应用并启用" : "启用热键";
            enableHotkeys.Enabled = !registered || changed; disableHotkeys.Enabled = registered;
            trayHotkeyToggle.Text = registered ? "关闭热键" : "启用热键";
            tray.Text = registered ? "顺序贴 · 热键已启用" : "顺序贴 · 热键已关闭";
            RefreshMiniWindow();
        }
        void SetHotkeysEnabled(bool enabled)
        {
            if (enabled)
            {
                CommitShortcuts(true);
            }
            else
            {
                // Release every Windows registration, including the temporary Esc key,
                // rather than merely ignoring hotkey messages and swallowing the keys.
                registered = false; UnregisterAll(); Cancel("热键已关闭。");
                model.HotkeysDisabled = true; ShowShortcutSummary();
                if (Save()) SetStatus("热键已关闭，正在执行的操作已停止。F1 等按键恢复原来的正常用途。", false);
            }
        }
        void SetStatus(string text, bool error)
        {
            status.Text = text; status.ForeColor = error ? Color.FromArgb(173, 55, 42) : Muted;
            if (miniWindow != null && !miniWindow.IsDisposed) miniWindow.ShowNotice(text, error);
        }
        void OptionsChanged(object sender, EventArgs e)
        {
            if (!ready) return;
            model.CaptureCopies = capture.Checked; model.LoopSingle = loop.Checked; model.IntervalMs = (int)interval.Value; model.Separator = separator.SelectedIndex; model.BatchLimit = (int)batchLimit.Value; Save();
            RefreshQueue(false);
        }
        bool Save()
        {
            saveTimer.Stop();
            try { StateStore.Write(statePath, model); saveProblem = false; return true; }
            catch (Exception ex) { saveProblem = true; SetStatus("保存失败，退出后可能丢失本次修改：" + ex.Message, true); return false; }
        }
        void SaveProgressSoon() { saveTimer.Stop(); saveTimer.Start(); }
        void RefreshQueue(bool contentsChanged = true)
        {
            if (contentsChanged)
            {
                int selected = queue.SelectedIndex;
                refreshingQueue = true; queue.BeginUpdate(); queue.Items.Clear();
                foreach (var item in model.Items) queue.Items.Add(item);
                if (selected >= 0 && model.Items.Count > 0) queue.SelectedIndex = Math.Min(selected, model.Items.Count - 1);
                queue.EndUpdate(); refreshingQueue = false;
            }
            queue.Invalidate();
            progress.Text = QueueRules.Progress(model); RefreshMiniWindow();
        }
        void DrawQueueItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= model.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0, next = e.Index == QueueRules.Start(model.NextIndex, model.Items.Count, model.LoopSingle);
            using (var brush = new SolidBrush(selected ? Color.FromArgb(220, 232, 253) : next ? Color.FromArgb(239, 245, 255) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
            string preview = model.Items[e.Index].Replace("\r\n", " ↵ ").Replace("\n", " ↵ ").Replace("\r", " ↵ ").Replace("\t", " ⇥ ");
            var rect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, (next ? "▶ " : "   ") + (e.Index + 1).ToString("D2") + "   " + preview, Font, rect, next ? Blue : Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
        void AddItems(IEnumerable<string> items)
        {
            var values = items.Where(x => !String.IsNullOrEmpty(x)).ToArray();
            if (values.Length == 0) { SetStatus("先在右侧输入需要加入队列的文本。", true); return; }
            model.Items.AddRange(values); RefreshQueue(); if (Save()) SetStatus("已添加 " + values.Length + " 条文本。", false);
        }
        void UpdateSelected()
        {
            int index = queue.SelectedIndex;
            if (index < 0) { SetStatus("先选择左侧要修改的条目。", true); return; }
            if (String.IsNullOrEmpty(editor.Text)) { SetStatus("条目不能为空；移除请用“删除所选”。", true); return; }
            EditQueueItem(index, editor.Text);
        }
        internal bool EditQueueItem(int index, string text)
        {
            if (action >= 0 || index < 0 || index >= model.Items.Count || String.IsNullOrEmpty(text)) return false;
            model.Items[index] = text;
            if (queue.SelectedIndex == index) editor.Text = text;
            RefreshQueue(); if (Save()) SetStatus("第 " + (index + 1) + " 条已更新。", false);
            return true;
        }
        internal void SetNextQueueItem(int index)
        {
            if (action >= 0 || index < 0 || index >= model.Items.Count) return;
            model.NextIndex = index; RefreshQueue(false); if (Save()) SetStatus("下次从第 " + (index + 1) + " 条粘贴。", false);
        }
        internal int MoveQueueItem(int index, int delta)
        {
            if (action >= 0) return index;
            int moved = QueueRules.Move(model, index, delta);
            if (moved == index) return index;
            RefreshQueue(); queue.SelectedIndex = moved;
            if (Save()) SetStatus("顺序已调整，下一条标记跟随原来的文本。", false);
            return moved;
        }
        void MoveItem(int delta)
        {
            MoveQueueItem(queue.SelectedIndex, delta);
        }
        void DeleteSelected()
        {
            RemoveQueueItem(queue.SelectedIndex);
        }
        internal bool RemoveQueueItem(int index)
        {
            if (action >= 0 || !QueueRules.Remove(model, index)) return false;
            RefreshQueue();
            if (queue.SelectedIndex >= 0) editor.Text = model.Items[queue.SelectedIndex]; else editor.Clear();
            if (Save()) SetStatus("已删除条目，下一条位置已同步。", false);
            return true;
        }
        void ClearItems()
        {
            RequestClearItems(this);
        }
        internal void RequestClearItems(IWin32Window owner)
        {
            if (action >= 0 || model.Items.Count == 0) return;
            queueDialogOpen = true;
            try { if (MessageBox.Show(owner, "清空队列中的 " + model.Items.Count + " 条文本？", "清空队列", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) ClearQueueItems(); }
            finally { queueDialogOpen = false; }
        }
        internal void ClearQueueItems()
        {
            if (action >= 0) return;
            model.Items.Clear(); model.NextIndex = 0; editor.Clear(); RefreshQueue(); if (Save()) SetStatus("队列已清空。", false);
        }
        void ImportText(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = "导入 UTF-8 文本（每个非空行为一条，追加到队列）", Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { AddItems(QueueRules.Lines(File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)))); }
                    catch (Exception ex) { SetStatus("导入失败，请确认文件为 UTF-8 或带 BOM 的 Unicode 文本：" + ex.Message, true); }
        }
        void ExportQueue(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog { Title = "导出队列（保留多行条目）", Filter = "顺序贴队列 (*.json)|*.json", FileName = "我的文本队列.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try
                    {
                        using (var stream = File.Create(dialog.FileName)) new DataContractJsonSerializer(typeof(List<string>)).WriteObject(stream, model.Items);
                        SetStatus("队列已导出。", false);
                    }
                    catch (Exception ex) { SetStatus("导出失败：" + ex.Message, true); }
        }
        void ImportQueue(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = "导入队列（追加到当前队列）", Filter = "顺序贴队列 (*.json)|*.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try
                    {
                        List<string> items;
                        using (var stream = File.OpenRead(dialog.FileName)) items = (List<string>)new DataContractJsonSerializer(typeof(List<string>)).ReadObject(stream);
                        if (items == null) throw new InvalidDataException("队列文件为空。"); AddItems(items);
                    }
                    catch (Exception ex) { SetStatus("导入失败：" + ex.Message, true); }
        }
        void UnregisterAll() { for (int i = 0; i < 4; i++) Native.UnregisterHotKey(Handle, HotkeyBase + i); }
        bool RegisterAll(Shortcut[] shortcuts, out string error)
        {
            error = Shortcut.Validate(shortcuts); if (error != null) return false;
            for (int i = 0; i < 4; i++)
                if (!Native.RegisterHotKey(Handle, HotkeyBase + i, (uint)shortcuts[i].Modifiers | 0x4000, (uint)shortcuts[i].Key))
                { UnregisterAll(); error = "“" + Shortcut.ActionNames[i] + "”的 " + shortcuts[i] + " 被其他程序占用或不可用，请关闭占用软件的热键或更换按键。"; return false; }
            return true;
        }
        void ApplyShortcuts(object sender, EventArgs e)
        {
            CommitShortcuts(registered);
        }
        void CommitShortcuts(bool enable)
        {
            var pending = shortcutEditors.Select(x => x.Get()).ToArray(); string error = Shortcut.Validate(pending);
            if (error != null) { ShowShortcutSummary(); SetStatus("未应用。" + error + (registered ? " 当前仍使用顶部显示的旧快捷键。" : " 热键仍然关闭。"), true); return; }
            if (action >= 0) Cancel("正在应用快捷键，已停止当前操作。");
            if (!enable)
            {
                model.Hotkeys = pending; model.HotkeysDisabled = true; ShowShortcutSummary();
                if (Save()) SetStatus("快捷键设置已保存，热键仍然关闭。点击“启用热键”后生效。", false);
                return;
            }
            bool wasRegistered = registered;
            UnregisterAll();
            if (RegisterAll(pending, out error)) { model.Hotkeys = pending; registered = true; model.HotkeysDisabled = false; }
            else
            {
                string restoreError; registered = wasRegistered && RegisterAll(model.Hotkeys, out restoreError);
                if (!registered) { model.HotkeysDisabled = true; Save(); }
                ShowShortcutSummary();
                hotkeyDraft.Text = "未应用：" + error; hotkeyDraft.ForeColor = Color.FromArgb(173, 55, 42);
                SetStatus(error + (registered ? " 当前仍使用顶部显示的旧快捷键。" : " 当前热键关闭。"), true);
                return;
            }
            ShowShortcutSummary();
            if (Save()) SetStatus("热键已启用：复制 " + pending[0] + "，顺序粘贴 " + pending[2] + "。切换到目标程序即可使用。", false);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == EscapeId) { Cancel("已按 Esc 停止，已粘贴的内容保留。"); return; }
                if (id >= HotkeyBase && id < HotkeyBase + 4) { StartAction(id - HotkeyBase); return; }
            }
            base.WndProc(ref m);
        }
        void StartAction(int requested)
        {
            if (!registered) return;
            if (queueDialogOpen || (miniWindow != null && (miniWindow.IsEditing || miniWindow.IsDragging))) { SetStatus("请先完成当前条目的编辑、拖动或清空确认。", true); return; }
            if (action >= 0) { if (requested == 3 && action == 3) Cancel("已停止批量粘贴。"); return; }
            target = desktop.ForegroundWindow;
            if (target == IntPtr.Zero || desktop.IsOwnWindow(target)) { SetStatus("先切换到需要复制或粘贴的目标程序，再按快捷键。", true); return; }
            if (requested >= 2)
            {
                int start = QueueRules.Start(model.NextIndex, model.Items.Count, requested == 2 && model.LoopSingle);
                if (start < 0) { SetStatus(model.Items.Count == 0 ? "队列为空，请先复制文本或手动添加条目。" : "已到队列末尾。点击“从第 1 条开始”或设置下一条。", true); tray.ShowBalloonTip(1800, "顺序贴", status.Text, ToolTipIcon.Info); return; }
                snapshot = model.Items.ToArray(); batchPosition = start; batchEnd = requested == 2 ? start + 1 : QueueRules.End(start, snapshot.Length, model.BatchLimit);
            }
            if (requested == 3)
            {
                escapeRegistered = Native.RegisterHotKey(Handle, EscapeId, 0x4000, (uint)Keys.Escape);
                if (!escapeRegistered) { SetStatus("Esc 停止键被占用，本次未开始批量粘贴。", true); return; }
            }
            action = requested; stage = 0; pastedCount = 0; trigger = (Keys)model.Hotkeys[requested].Key;
            releaseDeadline = desktop.UtcNow.AddSeconds(5); stop.Enabled = requested == 3;
            foreach (var c in mutationControls) c.Enabled = false;
            RefreshMiniWindow();
            SetStatus(requested == 3 ? "批量粘贴中，按 Esc 停止；切换窗口会自动停止。" : "正在处理快捷键…", false);
            timer.Start();
            Tick(this, EventArgs.Empty);
        }
        void Tick(object sender, EventArgs e)
        {
            try
            {
                if (action < 0) return;
                if (desktop.ForegroundWindow != target) { Cancel("目标窗口已切换，操作已停止。"); return; }
                if (action == 3 && desktop.Down(Keys.Escape)) { Cancel("已停止批量粘贴。"); return; }
                if (stage == 0)
                {
                    if (desktop.ModifiersDown() || desktop.Down(trigger)) { if (desktop.UtcNow > releaseDeadline) Cancel("快捷键未松开，本次操作已取消。"); return; }
                    if (action == 0) { oldClipboard = desktop.ClipboardSequence; if (!desktop.Press(Keys.C, true)) { InputFailed(); return; } stage = 1; due = desktop.UtcNow.AddSeconds(2); return; }
                    if (action == 1) { if (!desktop.Press(Keys.V, true)) { InputFailed(); return; } Finish("已发送粘贴。"); return; }
                    PasteItem(); return;
                }
                if (stage == 1)
                {
                    if (desktop.ClipboardSequence != oldClipboard)
                    {
                        try
                        {
                            string value = desktop.ReadClipboardText();
                            if (value != null)
                            {
                                if (model.CaptureCopies && value.Length > 0) { model.Items.Add(value); RefreshQueue(); Save(); }
                                Finish(model.CaptureCopies && value.Length > 0 ? "复制完成，已加入队列第 " + model.Items.Count + " 条。" : "复制完成。"); return;
                            }
                        }
                        catch (ExternalException) { }
                    }
                    if (desktop.UtcNow >= due) Cancel("没有收到新的文本：请先选中文本，确认目标程序支持 Ctrl+C。");
                    return;
                }
                if (desktop.ModifiersDown()) { Cancel("检测到键盘操作，已停止粘贴。"); return; }
                if (desktop.UtcNow < due) return;
                if (stage == 2)
                {
                    if (batchPosition >= batchEnd) { Finish("已粘贴 " + pastedCount + " 条。" + (model.NextIndex >= model.Items.Count ? "队列已到末尾。" : "下次从第 " + (model.NextIndex + 1) + " 条继续。")); return; }
                    if (model.Separator != 0)
                    {
                        if (!desktop.Press(model.Separator == 1 ? Keys.Tab : Keys.Return, false)) { InputFailed(); return; }
                        stage = 3; due = desktop.UtcNow.AddMilliseconds(Math.Max(150, model.IntervalMs / 2)); return;
                    }
                    PasteItem(); return;
                }
                if (stage == 3) PasteItem();
            }
            catch (Exception ex) { Cancel("操作停止：" + ex.Message); }
        }
        void PasteItem()
        {
            if (desktop.ForegroundWindow != target) { Cancel("目标窗口已切换，操作已停止。"); return; }
            try { desktop.SetClipboardText(snapshot[batchPosition]); }
            catch (ExternalException) { Cancel("剪贴板正被其他程序占用，本条未粘贴，请重试。"); return; }
            if (desktop.ForegroundWindow != target || desktop.ModifiersDown()) { Cancel("焦点或按键状态发生变化，本条未粘贴。"); return; }
            if (!desktop.Press(Keys.V, true)) { InputFailed(); return; }
            batchPosition++; pastedCount++; model.NextIndex = batchPosition;
            RefreshQueue(false); SaveProgressSoon();
            // Only automated batches need an inter-item delay. A completed manual
            // paste must be ready for the user's next key press immediately.
            if (action == 2) { Finish("已粘贴 1 条。" + QueueRules.Progress(model)); return; }
            stage = 2; due = desktop.UtcNow.AddMilliseconds(model.IntervalMs);
        }
        void InputFailed() { Cancel("系统未接受粘贴按键。请确认本工具和目标程序权限相同，然后重试。"); }
        void Finish(string message)
        {
            timer.Stop(); action = -1; snapshot = null;
            if (escapeRegistered) { Native.UnregisterHotKey(Handle, EscapeId); escapeRegistered = false; }
            foreach (var c in mutationControls) c.Enabled = true;
            stop.Enabled = false;
            RefreshMiniWindow();
            if (!saveProblem) SetStatus(message, false);
        }
        void Cancel(string message) { Finish(message); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                base.OnFormClosing(e);
                return;
            }
            base.OnFormClosing(e);
            if (e.Cancel) { exitRequested = false; return; }
            if (!closing)
            {
                closing = true;
                if (miniWindow != null && !miniWindow.IsDisposed) { SaveMiniBounds(miniWindow.Bounds); miniWindow.CloseForExit(); }
                Finish("已退出。"); UnregisterAll(); Save(); tray.Visible = false; tray.Dispose(); timer.Dispose(); saveTimer.Dispose();
            }
        }
    }

    internal static class SelfTests
    {
        public static int Run(string directory)
        {
            int passed = 0;
            Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); passed++; };
            try
            {
                check(QueueRules.Start(0, 0, true) == -1, "empty queue");
                check(QueueRules.Start(3, 3, false) == -1, "end stops");
                check(QueueRules.Start(3, 3, true) == 0, "single loop");
                check(QueueRules.Start(1, 3, false) == 1, "resume cursor");
                check(QueueRules.End(1, 5, 0) == 5, "all remaining");
                check(QueueRules.End(1, 5, 2) == 3, "batch limit");
                check(QueueRules.End(4, 5, 8) == 5, "batch clamp");
                check(QueueRules.Lines("甲\r\n乙\n\n 丙 \r丁").SequenceEqual(new[] { "甲", "乙", " 丙 ", "丁" }), "unicode lines and whitespace");
                check(Shortcut.Validate(AppState.Defaults()) == null, "default shortcuts");
                var shortcuts = AppState.Defaults(); shortcuts[1] = shortcuts[0].Copy(); check(Shortcut.Validate(shortcuts) != null, "duplicate rejection");
                shortcuts = AppState.Defaults(); shortcuts[0] = new Shortcut(2, Keys.C); check(Shortcut.Validate(shortcuts) != null, "recursive Ctrl+C rejection");
                shortcuts = AppState.Defaults(); shortcuts[0] = new Shortcut(0, Keys.A); check(Shortcut.Validate(shortcuts) != null, "plain letter rejection");
                shortcuts[0] = new Shortcut(0, Keys.F8); check(Shortcut.Validate(shortcuts) == null, "function key acceptance");
                shortcuts[0] = new Shortcut(0, Keys.F1); check(Shortcut.Validate(shortcuts) == null, "standalone F1 acceptance");
                shortcuts[0] = new Shortcut(0, Keys.F12); check(Shortcut.Validate(shortcuts) != null, "reserved F12 rejection");
                check(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "SendInput native layout");
                Directory.CreateDirectory(directory); string path = Path.Combine(directory, "test-settings.json");
                var value = new AppState { Items = new List<string> { "第一条", "换行\r\n保留\t🙂", "  保留空格  " }, NextIndex = 1, IntervalMs = 700 };
                StateStore.Write(path, value); var loaded = StateStore.Read(path);
                check(loaded.Items.SequenceEqual(value.Items) && loaded.NextIndex == 1 && loaded.IntervalMs == 700, "state roundtrip");
                value.NextIndex = 2; StateStore.Write(path, value); check(StateStore.Read(path + ".bak").NextIndex == 1 && StateStore.Read(path).NextIndex == 2, "atomic backup");
                value.IntervalMs = -1; value.NextIndex = 900; value.Normalize(); check(value.IntervalMs == 150 && value.NextIndex == 3, "state bounds");
                value.HotkeysDisabled = true; StateStore.Write(path, value); check(StateStore.Read(path).HotkeysDisabled, "disabled state survives restart");
                value.HotkeysDisabled = false; StateStore.Write(path, value); check(!StateStore.Read(path).HotkeysDisabled, "enabled state survives restart");
                string legacyPath = Path.Combine(directory, "legacy-settings.json");
                File.WriteAllText(legacyPath, "{\"Version\":1,\"Items\":[\"legacy\"],\"NextIndex\":0,\"IntervalMs\":500}");
                check(!StateStore.Read(legacyPath).HotkeysDisabled, "old settings retain enabled default");
                var q = new AppState { Items = new List<string> { "甲", "乙", "丙", "丁" }, NextIndex = 2 };
                check(QueueRules.Move(q, 2, -2) == 0 && q.Items[q.NextIndex] == "丙" && q.NextIndex == 0, "moving upcoming item preserves identity");
                QueueRules.Move(q, 3, -3); check(q.Items[q.NextIndex] == "丙" && q.NextIndex == 1, "moving across cursor preserves upcoming item");
                QueueRules.Remove(q, 0); check(q.NextIndex == 0 && q.Items[0] == "丙", "deletion before cursor preserves upcoming item");
                QueueRules.Remove(q, 0); check(q.NextIndex == 0 && q.Items[0] == "甲", "deleting upcoming item advances to following item");
                q.NextIndex = q.Items.Count - 1; QueueRules.Remove(q, q.NextIndex); check(q.NextIndex == q.Items.Count && QueueRules.Start(q.NextIndex, q.Items.Count, false) == -1, "deleting final upcoming item ends sequence");
                q.Items = new List<string> { "same", "same", "tail" }; q.NextIndex = 1;
                QueueRules.Move(q, 0, 2); check(q.NextIndex == 0 && q.Items[q.NextIndex] == "same", "duplicate values do not confuse cursor identity");
                q.NextIndex = q.Items.Count; QueueRules.Move(q, 2, -2); check(q.NextIndex == q.Items.Count, "reordering completed queue does not replay items");
                q.LoopSingle = true; check(QueueRules.Progress(q).Contains("第 1 条") && QueueRules.Progress(q).Contains("循环"), "loop arrow and progress agree");
                q.LoopSingle = false; check(QueueRules.Progress(q).Contains("末尾"), "completed progress without loop");
                string before = String.Join("|", q.Items); QueueRules.Move(q, -1, 1); check(!QueueRules.Remove(q, 100) && String.Join("|", q.Items) == before, "invalid queue edits leave data unchanged");
                value.MiniHidden = true; value.MiniUnpinned = true; value.MiniBoundsSaved = true; value.MiniX = -350; value.MiniY = 120; value.MiniWidth = 360; value.MiniHeight = 460;
                StateStore.Write(path, value); loaded = StateStore.Read(path);
                check(loaded.MiniHidden && loaded.MiniUnpinned && loaded.MiniX == -350 && loaded.MiniWidth == 360, "mini preferences roundtrip");
                var screen = new Rectangle(-1920, 0, 1920, 1080);
                var bounds = MiniQueueForm.VisibleBounds(new Rectangle(4000, 4000, 360, 460), screen);
                check(screen.Contains(bounds), "mini window recovers onto available monitor");
                File.WriteAllText(Path.Combine(directory, "test-result.txt"), "PASS: " + passed + " checks\r\n", Encoding.UTF8); return 0;
            }
            catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "test-result.txt"), "FAIL after " + passed + " checks: " + ex, Encoding.UTF8); return 1; }
        }
    }

    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test") return SelfTests.Run(Path.GetFullPath(args[1]));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (var mutex = new Mutex(true, "Local\\SequencePasteStandalone_29821A7B", out created))
            {
                if (!created) { MessageBox.Show("顺序贴已经在运行。请从任务栏右下角的托盘图标打开。", "顺序贴"); return 0; }
                string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                Application.Run(new MainForm(data));
            }
            return 0;
        }
    }
}
