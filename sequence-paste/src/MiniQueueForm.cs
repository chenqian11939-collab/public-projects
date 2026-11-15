using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SequencePaste
{
    internal sealed class QueueListView : ListView
    {
        int dropGap = -1;
        public QueueListView() { DoubleBuffered = true; }
        public int DropGap { get { return dropGap; } set { if (dropGap != value) { dropGap = value; Invalidate(); } } }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Native ListView insertion marks do not support Details view.
            if (m.Msg == 0x000F && dropGap >= 0 && Items.Count > 0)
            {
                int y = dropGap < Items.Count ? Items[dropGap].Bounds.Top : Items[Items.Count - 1].Bounds.Bottom;
                if (y > 1 && y < ClientSize.Height)
                    using (var g = CreateGraphics())
                    using (var pen = new Pen(Color.FromArgb(35, 99, 220), 3))
                        g.DrawLine(pen, 2, y, ClientSize.Width - 3, y);
            }
        }
    }

    internal sealed class MiniQueueForm : Form
    {
        readonly MainForm host;
        readonly QueueListView list = new QueueListView();
        readonly Label progress = new Label();
        readonly ToolStripStatusLabel notice = new ToolStripStatusLabel();
        readonly Button hotkeys = new Button(), edit = new Button(), remove = new Button(), clear = new Button(), next = new Button();
        readonly CheckBox pinned = new CheckBox();
        readonly ToolTip tips = new ToolTip();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        string[] values = new string[0];
        int nextIndex = -2;
        bool busy, refreshing, exiting;
        internal bool IsEditing { get; private set; }
        internal bool IsDragging { get; private set; }
        DateTime lastDragScroll;
        internal sealed class QueueDrag
        {
            internal MiniQueueForm Source;
            internal int Index;
            internal string[] Original;
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        internal MiniQueueForm(MainForm host, AppState state)
        {
            this.host = host; Icon = host.Icon;
            Text = "顺序贴 · 迷你窗口"; Name = "MiniQueueWindow";
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.SizableToolWindow; ShowInTaskbar = false;
            MaximizeBox = false; MinimizeBox = false; MinimumSize = new Size(320, 300);
            BackColor = Color.FromArgb(246, 248, 252); TopMost = !state.MiniUnpinned;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromControl(host).WorkingArea;
            Rectangle desired = state.MiniBoundsSaved ? new Rectangle(state.MiniX, state.MiniY, Math.Max(320, state.MiniWidth), Math.Max(300, state.MiniHeight)) : new Rectangle(area.Right - 384, area.Top + 80, 360, 460);
            Bounds = VisibleBounds(desired, Screen.FromRectangle(desired).WorkingArea);
            BuildUi(); pinned.Checked = TopMost;
            pinned.CheckedChanged += delegate { TopMost = pinned.Checked; host.SetMiniPinned(TopMost); };
            ResizeEnd += delegate { host.SaveMiniBounds(Bounds); };
            Resize += delegate { ResizeColumns(); };
        }

        internal static Rectangle VisibleBounds(Rectangle desired, Rectangle area)
        {
            int width = Math.Min(Math.Max(320, desired.Width), area.Width);
            int height = Math.Min(Math.Max(300, desired.Height), area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(desired.X, area.Right - width)), Math.Max(area.Top, Math.Min(desired.Y, area.Bottom - height)), width, height);
        }
        static void StyleButton(Button button, string text)
        {
            button.Text = text; button.Dock = DockStyle.Fill; button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 219, 232);
            button.BackColor = Color.White; button.Margin = new Padding(2); button.Cursor = Cursors.Hand;
        }
        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8), Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); Controls.Add(root);
            var footer = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = true, BackColor = BackColor, ShowItemToolTips = true };
            notice.Spring = true; notice.TextAlign = ContentAlignment.MiddleLeft; notice.AutoToolTip = false;
            notice.ForeColor = Color.FromArgb(91, 107, 129); footer.Items.Add(notice); Controls.Add(footer);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            var main = new Button(); StyleButton(main, "显示 / 收起主窗"); main.Click += delegate { host.ToggleMainWindow(); }; toolbar.Controls.Add(main, 0, 0);
            StyleButton(hotkeys, "启用热键"); hotkeys.Click += delegate { host.ToggleHotkeys(); }; toolbar.Controls.Add(hotkeys, 1, 0);
            pinned.Text = "置顶"; pinned.Dock = DockStyle.Fill; pinned.Margin = new Padding(5, 2, 0, 2); toolbar.Controls.Add(pinned, 2, 0); root.Controls.Add(toolbar, 0, 0);
            progress.Dock = DockStyle.Fill; progress.TextAlign = ContentAlignment.MiddleLeft; progress.Font = new Font(Font, FontStyle.Bold);
            progress.ForeColor = Color.FromArgb(35, 99, 200); progress.AutoEllipsis = true; root.Controls.Add(progress, 0, 1);
            list.Name = "MiniQueueList"; list.AccessibleName = "迷你窗口文本队列"; list.Dock = DockStyle.Fill;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.GridLines = true; list.HeaderStyle = ColumnHeaderStyle.Nonclickable; list.ShowItemToolTips = true;
            list.AllowDrop = true; list.ItemDrag += StartDrag; list.DragEnter += DragOverList; list.DragOver += DragOverList;
            list.DragLeave += delegate { list.DropGap = -1; }; list.DragDrop += DropOnList;
            list.Columns.Add("序号", 60); list.Columns.Add("文本内容", 240); root.Controls.Add(list, 0, 2);
            list.SelectedIndexChanged += delegate { if (!refreshing) UpdateButtons(); };
            list.DoubleClick += delegate { EditSelected(); };
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (busy) return;
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.Control && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) { MoveSelected(e.KeyCode == Keys.Up ? -1 : 1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = e.SuppressKeyPress = true; }
            };
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 3, 0, 0) };
            for (int i = 0; i < 3; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            StyleButton(edit, "编辑"); StyleButton(remove, "删除"); StyleButton(clear, "清空");
            edit.Click += delegate { EditSelected(); }; remove.Click += delegate { DeleteSelected(); }; clear.Click += delegate { host.RequestClearItems(this); };
            actions.Controls.Add(edit, 0, 0); actions.Controls.Add(remove, 1, 0); actions.Controls.Add(clear, 2, 0); root.Controls.Add(actions, 0, 3);
            StyleButton(next, "从选中条目继续粘贴"); next.Click += delegate { if (!busy) host.SetNextQueueItem(SelectedIndex); };
            next.ForeColor = Color.FromArgb(35, 99, 200); root.Controls.Add(next, 0, 4);
            menu.Items.Add("编辑内容", null, delegate { EditSelected(); });
            menu.Items.Add("删除条目", null, delegate { DeleteSelected(); });
            menu.Items.Add("设为下一条", null, delegate { if (!busy) host.SetNextQueueItem(SelectedIndex); });
            menu.Items.Add("清空队列…", null, delegate { host.RequestClearItems(this); });
            menu.Opening += delegate { UpdateButtons(); menu.Items[0].Enabled = edit.Enabled; menu.Items[1].Enabled = remove.Enabled; menu.Items[2].Enabled = next.Enabled; menu.Items[3].Enabled = clear.Enabled; };
            list.ContextMenuStrip = menu;
            tips.SetToolTip(list, "按住鼠标左键拖动条目，蓝线表示放置位置；主窗口同步排序。双击编辑；红色箭头为下一条。");
            tips.SetToolTip(clear, "清空主窗口和迷你窗口的全部条目（会先确认）。");
        }
        int SelectedIndex { get { return list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0]; } }
        void ResizeColumns()
        {
            if (list.Columns.Count == 2) list.Columns[1].Width = Math.Max(120, list.ClientSize.Width - list.Columns[0].Width - SystemInformation.VerticalScrollBarWidth - 4);
        }
        void UpdateButtons()
        {
            int index = SelectedIndex;
            bool hasSelection = !busy && index >= 0 && index < values.Length;
            edit.Enabled = remove.Enabled = next.Enabled = hasSelection;
            clear.Enabled = !busy && values.Length > 0;
        }
        internal void RefreshFrom(AppState state, bool hotkeysEnabled, bool inProgress)
        {
            int selected = SelectedIndex, oldCount = values.Length, previousNext = nextIndex;
            int top = list.IsHandleCreated && list.TopItem != null ? list.TopItem.Index : -1;
            bool changed = !values.SequenceEqual(state.Items);
            if (changed) values = state.Items.ToArray();
            busy = inProgress; nextIndex = QueueRules.Start(state.NextIndex, values.Length, state.LoopSingle);
            if (changed)
            {
                refreshing = true; list.BeginUpdate(); list.Items.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    string preview = values[i].Replace("\r\n", " ↵ ").Replace("\n", " ↵ ").Replace("\r", " ↵ ").Replace("\t", " ⇥ ");
                    var row = new ListViewItem { UseItemStyleForSubItems = false, ToolTipText = values[i] };
                    row.SubItems.Add(preview); list.Items.Add(row); StyleRow(i);
                }
                if (selected >= 0 && values.Length > 0) list.Items[Math.Min(selected, values.Length - 1)].Selected = true;
                list.EndUpdate(); refreshing = false;
            }
            else if (previousNext != nextIndex) { StyleRow(previousNext); StyleRow(nextIndex); }
            progress.Text = QueueRules.Progress(state);
            tips.SetToolTip(progress, state.NextIndex >= values.Length && state.LoopSingle && values.Length > 0 ? "单条顺序粘贴将循环回第 1 条；批量粘贴仍停在末尾，可手动设为下一条。" : progress.Text);
            hotkeys.Text = hotkeysEnabled ? "关闭热键" : "启用热键";
            hotkeys.ForeColor = hotkeysEnabled ? Color.FromArgb(25, 123, 77) : Color.FromArgb(91, 107, 129);
            UpdateButtons(); ResizeColumns();
            if (list.IsHandleCreated && values.Length > 0)
            {
                if (nextIndex >= 0 && previousNext != nextIndex) list.EnsureVisible(nextIndex);
                else if (values.Length > oldCount) list.EnsureVisible(values.Length - 1);
                else if (changed && top >= 0) list.TopItem = list.Items[Math.Min(top, values.Length - 1)];
            }
            ShowNotice(busy ? "正在操作；Esc 停止批量" : "拖动排序 · 双击编辑 · 右下角缩放", false);
        }
        void StyleRow(int index)
        {
            if (index < 0 || index >= list.Items.Count) return;
            bool isNext = index == nextIndex; var row = list.Items[index];
            row.Text = (isNext ? "▶ " : "   ") + (index + 1);
            row.ForeColor = isNext ? Color.FromArgb(206, 53, 53) : Color.FromArgb(80, 94, 112);
            row.BackColor = isNext ? Color.FromArgb(231, 239, 255) : Color.White;
            row.SubItems[1].BackColor = row.BackColor; row.SubItems[1].ForeColor = Color.FromArgb(28, 42, 61);
        }
        internal void ShowNotice(string text, bool error)
        {
            notice.Text = text; notice.ForeColor = error ? Color.FromArgb(173, 55, 42) : Color.FromArgb(91, 107, 129); notice.ToolTipText = text;
        }
        void StartDrag(object sender, ItemDragEventArgs e)
        {
            var item = e.Item as ListViewItem;
            if (busy || IsEditing || item == null || e.Button != MouseButtons.Left) return;
            IsDragging = true;
            try { list.DoDragDrop(new QueueDrag { Source = this, Index = item.Index, Original = (string[])values.Clone() }, DragDropEffects.Move); }
            finally { IsDragging = false; list.DropGap = -1; }
        }
        QueueDrag ValidDrag(DragEventArgs e)
        {
            if (busy || e.Data == null || !e.Data.GetDataPresent(typeof(QueueDrag))) return null;
            var drag = e.Data.GetData(typeof(QueueDrag)) as QueueDrag;
            return drag != null && Object.ReferenceEquals(drag.Source, this) && drag.Index >= 0 && drag.Index < values.Length && values.SequenceEqual(drag.Original) ? drag : null;
        }
        int GapAt(Point point)
        {
            if (list.Items.Count == 0) return 0;
            var item = list.GetItemAt(8, point.Y);
            if (item != null) return item.Index + (point.Y >= item.Bounds.Top + item.Bounds.Height / 2 ? 1 : 0);
            var top = list.TopItem;
            return top != null && point.Y <= top.Bounds.Top ? top.Index : list.Items.Count;
        }
        void DragOverList(object sender, DragEventArgs e)
        {
            if (ValidDrag(e) == null) { e.Effect = DragDropEffects.None; list.DropGap = -1; return; }
            var point = list.PointToClient(new Point(e.X, e.Y));
            e.Effect = DragDropEffects.Move;
            if (list.TopItem != null && DateTime.UtcNow - lastDragScroll > TimeSpan.FromMilliseconds(100))
            {
                var top = list.TopItem;
                if (point.Y < top.Bounds.Top + 12 && top.Index > 0) list.EnsureVisible(top.Index - 1);
                else if (point.Y > list.ClientSize.Height - 18)
                {
                    int visible = Math.Max(1, (list.ClientSize.Height - top.Bounds.Top) / Math.Max(1, top.Bounds.Height));
                    list.EnsureVisible(Math.Min(list.Items.Count - 1, top.Index + visible));
                }
                lastDragScroll = DateTime.UtcNow;
            }
            list.DropGap = GapAt(point);
        }
        void DropOnList(object sender, DragEventArgs e)
        {
            var drag = ValidDrag(e); list.DropGap = -1;
            if (drag == null) { e.Effect = DragDropEffects.None; return; }
            int gap = GapAt(list.PointToClient(new Point(e.X, e.Y)));
            int to = QueueRules.DropDestination(drag.Index, gap, values.Length);
            SelectItem(host.MoveQueueItem(drag.Index, to - drag.Index)); e.Effect = DragDropEffects.Move;
        }
        void SelectItem(int index)
        {
            if (index < 0 || index >= list.Items.Count) return;
            foreach (ListViewItem item in list.SelectedItems) item.Selected = false;
            list.Items[index].Selected = true; list.Items[index].Focused = true; list.EnsureVisible(index); UpdateButtons();
        }
        void EditSelected()
        {
            int index = SelectedIndex; if (busy || index < 0 || index >= values.Length) return;
            IsEditing = true;
            try
            {
                using (var dialog = new MiniTextEditor(index + 1, values[index], text => host.EditQueueItem(index, text)))
                { dialog.Icon = Icon; dialog.TopMost = TopMost; dialog.ShowDialog(this); }
            }
            finally { IsEditing = false; }
        }
        void DeleteSelected()
        {
            int index = SelectedIndex; if (busy || index < 0) return;
            if (host.RemoveQueueItem(index)) SelectItem(Math.Min(index, values.Length - 1));
        }
        void MoveSelected(int delta)
        {
            int index = SelectedIndex; if (busy || index < 0) return;
            SelectItem(host.MoveQueueItem(index, delta));
        }
        internal void CloseForExit() { exiting = true; Close(); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; host.SaveMiniBounds(Bounds); Hide(); host.MiniWindowHidden();
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); menu.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class MiniTextEditor : Form
    {
        internal MiniTextEditor(int number, string value, Func<string, bool> save)
        {
            Text = "编辑第 " + number + " 条"; Font = new Font("Microsoft YaHei UI", 10F);
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(480, 310); MinimumSize = new Size(380, 260);
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); Controls.Add(layout);
            var text = new TextBox { Text = value, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "条目完整内容" };
            var message = new Label { Text = "可修改、删减内容，也可保留多行文本。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(82, 32) };
            var apply = new Button { Text = "保存", Size = new Size(82, 32) };
            apply.Click += delegate
            {
                if (String.IsNullOrEmpty(text.Text)) { message.Text = "内容不能为空；需要移除请使用“删除”。"; return; }
                if (save(text.Text)) DialogResult = DialogResult.OK; else message.Text = "当前无法修改，请稍后再保存。";
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply); layout.Controls.Add(text, 0, 0); layout.Controls.Add(message, 0, 1); layout.Controls.Add(buttons, 0, 2);
            CancelButton = cancel; AcceptButton = apply;
        }
    }
}
