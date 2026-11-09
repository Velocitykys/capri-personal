using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri's mind page (batch 3b). Read-only view of the snapshot the bot pushes (mind-state.json) plus queued
    // edits (delete, edit, approve) that the bot applies on its next tick. Nothing here can block the app: every
    // read and click is wrapped so a bad file just shows a message.
    internal sealed partial class Mainframe
    {
        private MindSheet mindSheet;

        private void OpenMind()
        {
            try
            {
                if (mindSheet != null && !mindSheet.IsDisposed && mindSheet.Zoom != zoom) mindSheet.Close();
                if (mindSheet == null || mindSheet.IsDisposed) mindSheet = new MindSheet(this, zoom);
                mindSheet.RefreshMind();
                if (!mindSheet.Visible) mindSheet.Show(this);
                mindSheet.Activate();
            }
            catch (Exception)
            {
                MessageBox.Show(this, "Couldn't open Capri's mind just now.", "Capri's mind", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // One line in the list. Id is empty for read-only lines (the mood section).
        private sealed class MindRow
        {
            internal string Id = "", Text = "", Detail = "";
            internal bool Pending;
            public override string ToString() { return (Pending ? "[waiting for you]  " : "") + Text; }
        }

        // A very small JSON reader: objects become Dictionary, arrays List, numbers double, plus string, bool, null.
        private sealed class MindJson
        {
            private readonly string s;
            private int i;
            private MindJson(string text) { s = text; }

            internal static object Parse(string text)
            {
                try { MindJson reader = new MindJson(text); return reader.Value(0); }
                catch (Exception) { return null; }
            }

            private void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private object Value(int depth)
            {
                if (depth > 40) throw new FormatException();
                Skip();
                if (i >= s.Length) throw new FormatException();
                char c = s[i];
                if (c == '{')
                {
                    i++; Dictionary<string, object> map = new Dictionary<string, object>();
                    Skip(); if (i < s.Length && s[i] == '}') { i++; return map; }
                    while (true)
                    {
                        Skip(); string key = Str(); Skip();
                        if (i >= s.Length || s[i] != ':') throw new FormatException();
                        i++; map[key] = Value(depth + 1); Skip();
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == '}') { i++; return map; }
                        throw new FormatException();
                    }
                }
                if (c == '[')
                {
                    i++; List<object> list = new List<object>();
                    Skip(); if (i < s.Length && s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value(depth + 1)); Skip();
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == ']') { i++; return list; }
                        throw new FormatException();
                    }
                }
                if (c == '"') return Str();
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new FormatException();
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            private string Str()
            {
                if (i >= s.Length || s[i] != '"') throw new FormatException();
                i++; StringBuilder sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new FormatException();
                    char e = s[i++];
                    if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b'); else if (e == 'f') sb.Append('\f');
                    else if (e == 'u')
                    {
                        if (i + 4 > s.Length) throw new FormatException();
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4;
                    }
                    else sb.Append(e);
                }
                throw new FormatException();
            }

            internal static object Get(object node, string key)
            {
                Dictionary<string, object> map = node as Dictionary<string, object>;
                object value;
                return map != null && map.TryGetValue(key, out value) ? value : null;
            }
            internal static string GetText(object node, string key)
            {
                object value = Get(node, key);
                return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            internal static double GetNum(object node, string key)
            {
                object value = Get(node, key);
                return value is double ? (double)value : 0;
            }
            internal static bool GetFlag(object node, string key) { object value = Get(node, key); return value is bool && (bool)value; }
            internal static List<object> GetList(object node, string key) { List<object> list = Get(node, key) as List<object>; return list ?? new List<object>(); }
        }

        private sealed class MindSheet : Form
        {
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly ListBox items;
            private readonly Note status;
            private readonly Note detail;
            private readonly Key[] tabs;
            private readonly Key edit, delete, approve, off;
            private readonly string[] tabNames = { "Mood", "Memory", "Follow", "Diary", "Taste", "Skills", "Jokes", "Reads" };
            private readonly List<MindRow>[] rows = new List<MindRow>[8];
            private readonly HashSet<string> hidden = new HashSet<string>();
            private readonly Dictionary<string, string> edited = new Dictionary<string, string>();
            private int tab;

            internal MindSheet(Mainframe owner, float zoom)
            {
                this.owner = owner; Zoom = zoom;
                Text = "Capri's mind";
                Icon = owner.Icon; ShowInTaskbar = false; TopMost = true;
                FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None; BackColor = Ground; ForeColor = White;
                ClientSize = new Size(Z(620), Z(560));
                Heading(this, "CAPRI'S MIND", new Point(Z(22), Z(18)), Display(14f * zoom), Rose, 0);
                status = new Note(); status.Bounds = Box(24, 48, 572, 32); status.ForeColor = Faint; status.BackColor = Ground;
                status.Font = Type(8.5f * zoom); Controls.Add(status);
                tabs = new Key[8];
                for (int t = 0; t < 8; t++)
                {
                    int which = t;
                    tabs[t] = new Key(); tabs[t].Text = tabNames[t]; tabs[t].Bounds = Box(22 + t * 72, 86, 68, 26);
                    tabs[t].Click += delegate { tab = which; ShowTab(); }; Controls.Add(tabs[t]);
                }
                items = new ListBox();
                items.Bounds = Box(22, 122, 576, 220); items.BackColor = Field; items.ForeColor = White;
                items.BorderStyle = BorderStyle.FixedSingle; items.Font = Type(9f * zoom); items.IntegralHeight = false;
                items.SelectedIndexChanged += delegate { UpdateSelection(); }; Controls.Add(items);
                detail = new Note(); detail.Bounds = Box(24, 350, 572, 92); detail.ForeColor = White; detail.BackColor = Ground;
                detail.Font = Type(8.5f * zoom); Controls.Add(detail);
                edit = new Key(); edit.Text = "Edit"; edit.Accent = Cyan; edit.Bounds = Box(22, 452, 80, 28);
                edit.Click += delegate { EditSelected(); }; Controls.Add(edit);
                delete = new Key(); delete.Text = "Forget"; delete.Accent = Hot; delete.Bounds = Box(110, 452, 80, 28);
                delete.Click += delegate { DeleteSelected(); }; Controls.Add(delete);
                approve = new Key(); approve.Text = "Approve"; approve.Accent = Cyan; approve.Bounds = Box(198, 452, 90, 28);
                approve.Click += delegate { ApproveSelected(); }; Controls.Add(approve);
                Key refresh = new Key(); refresh.Text = "Refresh"; refresh.Bounds = Box(296, 452, 80, 28);
                refresh.Click += delegate { RefreshMind(); }; Controls.Add(refresh);
                off = new Key(); off.Text = "Stop everything autonomous"; off.Accent = Hot; off.Bounds = Box(22, 500, 260, 32);
                off.Click += delegate { MasterOff(); }; Controls.Add(off);
                Key close = new Key(); close.Text = "Done"; close.Accent = Cyan; close.Loud = true;
                close.Bounds = Box(498, 500, 100, 32); close.Click += delegate { Close(); }; Controls.Add(close);
                for (int r = 0; r < rows.Length; r++) rows[r] = new List<MindRow>();
                RefreshMind();
            }

            internal void RefreshMind()
            {
                try
                {
                    for (int r = 0; r < rows.Length; r++) rows[r].Clear();
                    string path = owner.capri.MindStatePath();
                    object root = File.Exists(path) ? MindJson.Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
                    if (root == null)
                    {
                        status.Text = "Capri's bot hasn't sent anything yet. Start the Discord bot and give it a few minutes.";
                        rows[0].Add(new MindRow { Text = "Nothing to show yet." });
                    }
                    else Fill(root);
                }
                catch (Exception)
                {
                    status.Text = "Couldn't read Capri's notes just now. Try Refresh in a moment.";
                }
                UpdateOffButton();
                ShowTab();
            }

            private void Fill(object root)
            {
                double updated = MindJson.GetNum(root, "updatedAt");
                string when = updated > 0 ? Stamp(updated) : "unknown";
                string quiet = MindJson.GetText(root, "quiet");
                status.Text = "Last update from the bot: " + when + ".  Quiet hours: " + (quiet.Length == 0 ? "off" : quiet)
                    + " (change them on the Autonomy card).  Edits you make here reach her within about 5 minutes.";
                List<MindRow> mood = rows[0];
                mood.Add(new MindRow { Text = "Mood: " + MindJson.GetText(root, "mood") });
                object ownerMood = MindJson.Get(root, "ownerMood");
                string label = MindJson.GetText(ownerMood, "label");
                mood.Add(new MindRow { Text = "How she thinks you are: " + (label.Length == 0 || MindJson.GetNum(ownerMood, "level") < 0.3 ? "fine" : label) });
                Dictionary<string, object> emotions = MindJson.Get(root, "emotions") as Dictionary<string, object>;
                if (emotions != null)
                    foreach (KeyValuePair<string, object> pair in emotions)
                        if (pair.Value is double && (double)pair.Value >= 0.05)
                            mood.Add(new MindRow { Text = "  " + pair.Key + ": " + ((int)Math.Round((double)pair.Value * 100)).ToString(CultureInfo.InvariantCulture) + "%" });
                foreach (object ev in MindJson.GetList(root, "events"))
                    mood.Add(new MindRow { Text = "  " + Stamp(MindJson.GetNum(ev, "at")) + "  " + MindJson.GetText(ev, "emotion") + " — " + MindJson.GetText(ev, "cause") });
                string habits = MindJson.GetText(root, "habits");
                if (habits.Length > 0) mood.Add(new MindRow { Text = "Habits she has noticed: " + habits });
                AddRows(1, root, "memories", "text", "");
                AddRows(2, root, "followUps", "text", "dueAt");
                AddRows(3, root, "diary", "text", "");
                AddRows(4, root, "taste", "text", "");
                AddRows(5, root, "skills", "name", "steps");
                AddRows(6, root, "jokes", "text", "");
                AddRows(7, root, "curiosity", "topic", "text");
            }

            private void AddRows(int slot, object root, string list, string titleKey, string extraKey)
            {
                List<object> source = MindJson.GetList(root, list);
                for (int n = source.Count - 1; n >= 0; n--)
                {
                    object item = source[n];
                    string id = MindJson.GetText(item, "id");
                    if (id.Length == 0 || hidden.Contains(id)) continue;
                    MindRow row = new MindRow { Id = id, Pending = MindJson.GetFlag(item, "pending") };
                    string title = MindJson.GetText(item, titleKey);
                    string extra = extraKey.Length == 0 ? "" : MindJson.GetText(item, extraKey);
                    if (extraKey == "dueAt") extra = "check in around " + Stamp(MindJson.GetNum(item, "dueAt")) + (MindJson.GetFlag(item, "done") ? " (done)" : "");
                    string replaced;
                    if (edited.TryGetValue(id, out replaced)) { if (slot == 5) extra = replaced; else title = replaced; }
                    double at = MindJson.GetNum(item, "at");
                    row.Text = (at > 0 ? Stamp(at) + "  " : "") + title;
                    row.Detail = title + (extra.Length > 0 ? "\n" + extra : "");
                    rows[slot].Add(row);
                }
                if (rows[slot].Count == 0) rows[slot].Add(new MindRow { Text = "Nothing here yet." });
            }

            private static string Stamp(double ms)
            {
                try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture); }
                catch (Exception) { return "?"; }
            }

            private void ShowTab()
            {
                for (int t = 0; t < tabs.Length; t++) { tabs[t].Loud = t == tab; tabs[t].Invalidate(); }
                items.Items.Clear();
                foreach (MindRow row in rows[tab]) items.Items.Add(row);
                if (items.Items.Count > 0) items.SelectedIndex = 0;
                UpdateSelection();
            }

            private MindRow Selected()
            {
                return items.SelectedIndex >= 0 && items.SelectedIndex < items.Items.Count ? items.Items[items.SelectedIndex] as MindRow : null;
            }

            private void UpdateSelection()
            {
                MindRow row = Selected();
                bool real = row != null && row.Id.Length > 0;
                edit.Enabled = real;
                delete.Enabled = real;
                approve.Enabled = real && row.Pending;
                detail.Text = row == null ? "" : row.Detail.Length > 0 ? row.Detail : row.Text;
            }

            private void EditSelected()
            {
                MindRow row = Selected();
                if (row == null || row.Id.Length == 0) return;
                try
                {
                    string current = row.Detail.Length > 0 ? row.Detail : row.Text;
                    if (tab == 5) { int cut = current.IndexOf('\n'); current = cut >= 0 ? current.Substring(cut + 1) : current; }
                    using (MindTextSheet editor = new MindTextSheet(Zoom, tab == 5 ? "WHAT THIS SKILL DOES" : "WHAT SHOULD IT SAY?", current))
                    {
                        if (editor.ShowDialog(this) != DialogResult.OK) return;
                        owner.capri.MindQueueEdit("edit", row.Id, editor.Value);
                        edited[row.Id] = editor.Value;
                    }
                    RefreshMind();
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Couldn't save that edit.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            private void DeleteSelected()
            {
                MindRow row = Selected();
                if (row == null || row.Id.Length == 0) return;
                try
                {
                    if (MessageBox.Show(this, "Make Capri forget this?\n\n" + row.Text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    owner.capri.MindQueueEdit("delete", row.Id, "");
                    hidden.Add(row.Id);
                    RefreshMind();
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Couldn't queue that.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            private void ApproveSelected()
            {
                MindRow row = Selected();
                if (row == null || row.Id.Length == 0 || !row.Pending) return;
                try
                {
                    owner.capri.MindQueueEdit("approve", row.Id, "");
                    row.Pending = false; items.Invalidate();
                    UpdateSelection();
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Couldn't queue that.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            private void UpdateOffButton()
            {
                bool on = owner.capri.ExperimentalFeatureOn("autonomy");
                off.Text = on ? "Stop everything autonomous" : "Autonomy is off";
                off.Enabled = on;
            }

            private void MasterOff()
            {
                try
                {
                    if (MessageBox.Show(this, "Turn Autonomy off? Capri stops messaging, researching, learning and acting on her own until you turn it back on from the Autonomy card. Chatting and your own commands keep working.",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    if (!owner.capri.SetExperimentalFeature("autonomy", false))
                        MessageBox.Show(this, "Couldn't switch that off. Use the Autonomy card instead.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Couldn't switch that off. Use the Autonomy card instead.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                UpdateOffButton();
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }
            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e); Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); float scale = ScaleOf(this); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(e.Graphics, this, scale); Edging(e.Graphics, this, scale);
            }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; } base.OnKeyDown(e); }
        }

        // Small text box dialog for editing one memory, diary line or skill.
        private sealed class MindTextSheet : Form
        {
            internal string Value { get { return box.Text.Trim(); } }
            private readonly TextBox box;
            private readonly float scale;

            internal MindTextSheet(float zoom, string heading, string current)
            {
                scale = zoom;
                Text = "Edit";
                FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; BackColor = Ground; ForeColor = White;
                ClientSize = new Size(Z(440), Z(220));
                Label caption = new Note(); caption.Text = heading;
                caption.ForeColor = Cyan; caption.BackColor = Ground; caption.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                caption.Bounds = Box(18, 16, 400, 18); Controls.Add(caption);
                box = new TextBox(); box.Multiline = true; box.ScrollBars = ScrollBars.Vertical; box.Text = current;
                box.Bounds = Box(18, 40, 404, 120); box.Font = Type(9f * zoom); box.BackColor = Field; box.ForeColor = White;
                box.BorderStyle = BorderStyle.FixedSingle; Controls.Add(box);
                Button save = new Button(); save.Text = "SAVE"; save.DialogResult = DialogResult.OK; save.Bounds = Box(322, 176, 100, 28);
                save.FlatStyle = FlatStyle.Flat; save.BackColor = Field; save.ForeColor = Cyan; save.FlatAppearance.BorderColor = Edge;
                save.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                save.Enabled = Value.Length > 0; box.TextChanged += delegate { save.Enabled = Value.Length > 0; };
                Controls.Add(save); AcceptButton = save;
                Button cancel = new Button(); cancel.Text = "CANCEL"; cancel.DialogResult = DialogResult.Cancel;
                cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Field; cancel.ForeColor = White; cancel.FlatAppearance.BorderColor = Edge;
                cancel.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                cancel.Bounds = Box(212, 176, 100, 28); Controls.Add(cancel); CancelButton = cancel;
            }
            private int Z(int value) { return (int)Math.Round(value * scale); }
            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }
        }
    }
}
