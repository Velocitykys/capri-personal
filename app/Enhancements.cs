using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class EnhancementActivity
    {
        [DataMember] internal string Kind;
        [DataMember] internal string Detail;
        [DataMember] internal string At;
    }

    // Experimental switches and their options are kept outside settings.json, so an experiment can never damage
    // Capri's established preferences.
    internal sealed partial class Companion
    {
        private readonly object enhancementGate = new object();
        private Dictionary<string, bool> enhancementFlags;
        private Dictionary<string, string> enhancementOptions;
        private List<EnhancementActivity> enhancementActivity;

        internal bool ExperimentalFeatureOn(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                bool on;
                if (enhancementFlags.TryGetValue(id, out on)) return on;
                return id == "custom_commands" || id == "immersive_routines" || id == "better_timers"
                    || id == "memory_explorer" || id == "pc_health_monitor";
            }
        }

        internal void RecordEnhancementActivity(string kind, string detail)
        {
            lock (enhancementGate)
            {
                if (enhancementActivity == null) enhancementActivity = ReadEnhancementActivity();
                enhancementActivity.Add(new EnhancementActivity { Kind = kind ?? "Activity", Detail = detail ?? "", At = DateTime.Now.ToString("o") });
                if (enhancementActivity.Count > 500) enhancementActivity.RemoveRange(0, enhancementActivity.Count - 500);
                WriteEnhancementFile(settingsPath + ".activity.json", enhancementActivity);
            }
        }

        internal string EnhancementActivityText(string kind)
        {
            lock (enhancementGate)
            {
                if (enhancementActivity == null) enhancementActivity = ReadEnhancementActivity();
                List<string> lines = new List<string>();
                for (int i = enhancementActivity.Count - 1; i >= 0 && lines.Count < 40; i--)
                {
                    EnhancementActivity item = enhancementActivity[i];
                    if (kind != null && !string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime at;
                    string when = DateTime.TryParse(item.At, out at) ? at.ToString("dd MMM HH:mm") : "";
                    lines.Add(when + "  " + item.Kind + ": " + item.Detail);
                }
                return lines.Count == 0 ? "Nothing recorded yet." : string.Join("\n", lines.ToArray());
            }
        }

        private List<EnhancementActivity> ReadEnhancementActivity()
        {
            try
            {
                using (FileStream stream = File.OpenRead(settingsPath + ".activity.json"))
                {
                    List<EnhancementActivity> saved = (List<EnhancementActivity>)new DataContractJsonSerializer(typeof(List<EnhancementActivity>)).ReadObject(stream);
                    return saved ?? new List<EnhancementActivity>();
                }
            }
            catch (Exception) { return new List<EnhancementActivity>(); }
        }

        internal bool ExperimentalFeatureOn(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                bool on;
                return enhancementFlags.TryGetValue(id, out on) && on;
            }
        }

        internal bool SetExperimentalFeature(string id, bool on)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                bool existed = enhancementFlags.ContainsKey(id), previous;
                enhancementFlags.TryGetValue(id, out previous);
                enhancementFlags[id] = on;
                if (WriteEnhancementFlags()) return true;
                if (existed) enhancementFlags[id] = previous;
                else enhancementFlags.Remove(id);
                return false;
            }
        }

        internal string ExperimentalOption(string id, string option, string fallback)
        {
            lock (enhancementGate)
            {
                if (enhancementOptions == null) enhancementOptions = ReadEnhancementOptions();
                string value;
                return enhancementOptions.TryGetValue(id + "." + option, out value) ? value : fallback;
            }
        }

        internal bool SetExperimentalOption(string id, string option, string value)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(option) || value == null) return false;
            lock (enhancementGate)
            {
                if (enhancementOptions == null) enhancementOptions = ReadEnhancementOptions();
                string key = id + "." + option, previous;
                bool existed = enhancementOptions.TryGetValue(key, out previous);
                enhancementOptions[key] = value;
                if (WriteEnhancementOptions()) return true;
                if (existed) enhancementOptions[key] = previous;
                else enhancementOptions.Remove(key);
                return false;
            }
        }

        internal bool ResetExperimentalFeatures()
        {
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                if (enhancementOptions == null) enhancementOptions = ReadEnhancementOptions();
                Dictionary<string, bool> oldFlags = new Dictionary<string, bool>(enhancementFlags);
                Dictionary<string, string> oldOptions = new Dictionary<string, string>(enhancementOptions);
                enhancementFlags.Clear();
                enhancementOptions.Clear();
                if (WriteEnhancementFlags() && WriteEnhancementOptions()) return true;
                enhancementFlags = oldFlags;
                enhancementOptions = oldOptions;
                WriteEnhancementFlags();
                WriteEnhancementOptions();
                return false;
            }
        }

        private Dictionary<string, bool> ReadEnhancementFlags()
        {
            try
            {
                using (FileStream stream = File.OpenRead(settingsPath + ".enhancements.json"))
                {
                    Dictionary<string, bool> saved = (Dictionary<string, bool>)new DataContractJsonSerializer(typeof(Dictionary<string, bool>)).ReadObject(stream);
                    return saved ?? new Dictionary<string, bool>();
                }
            }
            catch (Exception) { return new Dictionary<string, bool>(); }
        }

        private Dictionary<string, string> ReadEnhancementOptions()
        {
            try
            {
                using (FileStream stream = File.OpenRead(settingsPath + ".enhancements-options.json"))
                {
                    Dictionary<string, string> saved = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>)).ReadObject(stream);
                    return saved ?? new Dictionary<string, string>();
                }
            }
            catch (Exception) { return new Dictionary<string, string>(); }
        }

        private bool WriteEnhancementFlags()
        {
            return WriteEnhancementFile(settingsPath + ".enhancements.json", enhancementFlags);
        }

        private bool WriteEnhancementOptions()
        {
            return WriteEnhancementFile(settingsPath + ".enhancements-options.json", enhancementOptions);
        }

        private static bool WriteEnhancementFile<T>(string path, T value)
        {
            string temporary = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
                return false;
            }
        }
    }

    internal sealed partial class Mainframe
    {
        private EnhancementsSheet enhancements;

        private void OpenEnhancements()
        {
            if (enhancements != null && !enhancements.IsDisposed && enhancements.Zoom != zoom) enhancements.Close();
            if (enhancements == null || enhancements.IsDisposed) enhancements = new EnhancementsSheet(this, zoom);
            enhancements.Sync();
            if (!enhancements.Visible) enhancements.Show(this);
            enhancements.Activate();
        }

        private sealed class EnhancementOption
        {
            internal readonly string Id, Name, Default;
            internal readonly string[] Choices;
            internal EnhancementOption(string id, string name, string fallback, params string[] choices)
            {
                Id = id; Name = name; Default = fallback; Choices = choices;
            }
        }

        private sealed class Enhancement
        {
            internal readonly string Id, Name, Category, Description, Status;
            internal readonly EnhancementOption[] Options;
            internal Enhancement(string id, string name, string category, string description, string status, params EnhancementOption[] options)
            {
                Id = id; Name = name; Category = category; Description = description; Status = status; Options = options;
            }
        }

        private sealed class EnhancementsSheet : Form
        {
            internal const int Wide = 760, Tall = 700;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Enhancement[] features =
            {
                new Enhancement("custom_commands", "Custom Commands", "PRODUCTIVITY",
                    "Save your own trigger phrases and have Capri run a sequence of actions. Her existing routine engine is the starting point.",
                    "EXISTING ROUTINE ENGINE",
                    new EnhancementOption("confirm", "Confirm before running", "Risky actions", "Every command", "Risky actions", "Never")),
                new Enhancement("immersive_routines", "Immersive Routines", "AUTOMATION",
                    "Let Capri adapt to your day with routines that can respond to time, the apps you open and your activity.",
                    "AUTOMATIC TRIGGERS IN DEVELOPMENT",
                    new EnhancementOption("triggers", "Routine triggers", "Time + apps", "Manual only", "Time only", "Time + apps", "Context-aware")),
                new Enhancement("better_timers", "Better Timers & Reminders", "PRODUCTIVITY",
                    "Manage multiple timers, recurring reminders, snooze and rescheduling. Existing reminders are being extended rather than replaced.",
                    "EXISTING REMINDER ENGINE",
                    new EnhancementOption("snooze", "Default snooze", "10 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes"),
                    new EnhancementOption("sound", "Reminder sound", "On", "On", "Off")),
                new Enhancement("command_history", "Command History", "PRODUCTIVITY",
                    "Keep a searchable record of commands Capri receives, actions she takes and whether they succeeded.",
                    "ACTION LOG IN DEVELOPMENT",
                    new EnhancementOption("retention", "Keep history", "30 days", "7 days", "30 days", "90 days", "Forever")),
                new Enhancement("highlight_assistance", "Highlight Assistance", "PRODUCTIVITY",
                    "Select text in an app and ask Capri to explain, summarise, translate or rewrite just that selection instead of analysing your whole screen.",
                    "SELECTION CAPTURE IN DEVELOPMENT",
                    new EnhancementOption("shortcut", "Activation shortcut", "Ctrl + Alt + E", "Ctrl + Alt + E", "Ctrl + Shift + E", "Ctrl + Alt + H")),
                new Enhancement("personal_dashboard", "Personal Dashboard", "PRODUCTIVITY",
                    "A dedicated workspace for your calendar, tasks, reminders and upcoming events. Capri will be able to add and manage events from chat.",
                    "DASHBOARD IN DEVELOPMENT",
                    new EnhancementOption("view", "Default calendar view", "Week", "Day", "Week", "Month"),
                    new EnhancementOption("week_start", "Week starts on", "Monday", "Monday", "Sunday")),
                new Enhancement("milestone_reactions", "Milestone Reactions", "COMPANION",
                    "Let Capri notice meaningful progress and celebrate milestones without interrupting you for every small thing.",
                    "MILESTONE TRACKER IN DEVELOPMENT",
                    new EnhancementOption("frequency", "Reaction frequency", "Balanced", "Rare", "Balanced", "Frequent")),
                new Enhancement("shared_music", "Shared Music Discovery", "COMPANION",
                    "Discover songs and artists together, build shared playlists and keep track of music you both enjoyed.",
                    "MUSIC DISCOVERY IN DEVELOPMENT",
                    new EnhancementOption("service", "Music service", "Spotify", "Spotify")),
                new Enhancement("memory_explorer", "Memory Explorer", "COMPANION",
                    "Browse, search and manage what Capri remembers, including her notes and older chat transcripts. Nothing is uploaded by this panel.",
                    "EXISTING LOCAL MEMORY FILES",
                    new EnhancementOption("scope", "Search scope", "Memories + transcripts", "Memories only", "Memories + transcripts")),
                new Enhancement("notification_centre", "Notification Centre", "SYSTEM",
                    "One place for reminders, alerts, completed actions and important updates from Capri.",
                    "NOTIFICATION INBOX IN DEVELOPMENT",
                    new EnhancementOption("quiet", "Quiet hours", "Off", "Off", "22:00–08:00", "23:00–07:00")),
                new Enhancement("pc_health_monitor", "PC Health Monitor", "SYSTEM",
                    "See CPU load, memory use, running apps, drive space and battery status. Capri already has a PC usage reader and status tool.",
                    "EXISTING PC STATUS READER",
                    new EnhancementOption("refresh", "Refresh interval", "15 seconds", "5 seconds", "15 seconds", "30 seconds"),
                    new EnhancementOption("alerts", "High-usage alerts", "On", "On", "Off"))
            };

            private readonly Panel list;
            private readonly Toggle[] toggles;
            private readonly Key[] details;
            private readonly Label[] descriptions;
            private readonly Label[] statuses;
            private readonly Label[] categories;
            private readonly Key[][] optionKeys;
            private readonly Key[] actions;
            private readonly ContextMenuStrip[][] optionMenus;
            private readonly bool[] expanded;
            private bool syncing;

            internal EnhancementsSheet(Mainframe owner, float zoom)
            {
                this.owner = owner;
                Zoom = zoom;
                toggles = new Toggle[features.Length];
                details = new Key[features.Length];
                descriptions = new Label[features.Length];
                statuses = new Label[features.Length];
                categories = new Label[features.Length];
                optionKeys = new Key[features.Length][];
                actions = new Key[features.Length];
                optionMenus = new ContextMenuStrip[features.Length][];
                expanded = new bool[features.Length];

                Text = "Enhancements";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                TopMost = true;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96, 96);
                Font = Type(9f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Font small = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point);
                Heading(this, "ENHANCEMENTS >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Caption("Toggle each module on or off. Expand a module for its details and settings.", 28, 48, small, Faint);

                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);

                list = new Panel();
                list.Bounds = Box(20, 78, Wide - 40, Tall - 142);
                list.AutoScroll = true;
                list.BackColor = Ground;
                list.BorderStyle = BorderStyle.None;
                Controls.Add(list);

                for (int i = 0; i < features.Length; i++) AddFeature(i, small);

                Key reset = new Key();
                reset.Text = "Reset switches";
                reset.Font = Type(8.5f * zoom);
                reset.Accent = Hot;
                reset.Bounds = Box(28, Tall - 48, 126, 26);
                reset.Click += delegate
                {
                    if (!owner.capri.ResetExperimentalFeatures())
                        MessageBox.Show(this, "Couldn't save the reset. Your previous switches have been kept.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Sync();
                };
                Controls.Add(reset);

                Key done = new Key();
                done.Text = "Done";
                done.Font = Type(9f * zoom, true);
                done.Accent = Cyan;
                done.Loud = true;
                done.Bounds = Box(Wide - 128, Tall - 48, 100, 26);
                done.Click += delegate { Close(); };
                Controls.Add(done);

                LayoutFeatures();
                Sync();
            }

            private void AddFeature(int index, Font small)
            {
                Enhancement feature = features[index];
                categories[index] = new Note();
                categories[index].Text = feature.Category;
                categories[index].Font = Look.Mono(7.5f * Zoom, FontStyle.Bold, GraphicsUnit.Point);
                categories[index].ForeColor = Cyan;
                categories[index].BackColor = Ground;
                categories[index].AutoSize = false;
                categories[index].Bounds = Box(16, 0, 160, 14);
                list.Controls.Add(categories[index]);

                toggles[index] = new Toggle();
                toggles[index].Text = feature.Name;
                toggles[index].Font = Type(9.5f * Zoom, true);
                toggles[index].Switch = 96;
                toggles[index].Bounds = Box(16, 14, 450, 24);
                toggles[index].AccessibleName = feature.Name + ". " + feature.Description;
                toggles[index].CheckedChanged += delegate
                {
                    if (syncing) return;
                    if (!owner.capri.SetExperimentalFeature(feature.Id, toggles[index].Checked))
                    {
                        MessageBox.Show(this, "Couldn't save that switch. Its previous value has been restored.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Sync();
                    }
                    else Sync();
                };
                list.Controls.Add(toggles[index]);

                details[index] = new Key();
                details[index].Text = "Details";
                details[index].Font = Type(8f * Zoom);
                details[index].Bounds = Box(Wide - 190, 14, 130, 24);
                details[index].Chevron = true;
                details[index].AccessibleName = "Expand details and settings for " + feature.Name;
                details[index].Click += delegate
                {
                    expanded[index] = !expanded[index];
                    LayoutFeatures();
                };
                list.Controls.Add(details[index]);

                descriptions[index] = new Note();
                descriptions[index].Text = feature.Description;
                descriptions[index].Font = small;
                descriptions[index].ForeColor = White;
                descriptions[index].BackColor = Ground;
                descriptions[index].AutoSize = false;
                descriptions[index].AutoEllipsis = true;
                descriptions[index].Bounds = Box(28, 42, Wide - 100, 32);
                list.Controls.Add(descriptions[index]);

                statuses[index] = new Note();
                statuses[index].Text = feature.Status;
                statuses[index].Font = Look.Mono(7.5f * Zoom, FontStyle.Regular, GraphicsUnit.Point);
                statuses[index].ForeColor = Faint;
                statuses[index].BackColor = Ground;
                statuses[index].AutoSize = false;
                statuses[index].Bounds = Box(28, 75, Wide - 100, 14);
                list.Controls.Add(statuses[index]);

                optionKeys[index] = new Key[feature.Options.Length];
                optionMenus[index] = new ContextMenuStrip[feature.Options.Length];
                for (int optionIndex = 0; optionIndex < feature.Options.Length; optionIndex++)
                {
                    EnhancementOption option = feature.Options[optionIndex];
                    Key pick = new Key();
                    pick.Font = Type(8f * Zoom);
                    pick.Chevron = true;
                    pick.AccessibleName = option.Name;
                    int captured = optionIndex;
                    pick.Click += delegate
                    {
                        ContextMenuStrip menu = optionMenus[index][captured];
                        if (menu != null) menu.Show(pick, new Point(0, pick.Height));
                    };
                    list.Controls.Add(pick);
                    optionKeys[index][optionIndex] = pick;

                    ContextMenuStrip choices = Dropdown();
                    choices.AccessibleName = feature.Name + ": " + option.Name;
                    foreach (string value in option.Choices)
                    {
                        string selected = value;
                        choices.Items.Add(Choice(selected, false, delegate
                        {
                            if (!owner.capri.SetExperimentalOption(feature.Id, option.Id, selected))
                                MessageBox.Show(this, "Couldn't save that setting.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            Sync();
                        }));
                    }
                    optionMenus[index][optionIndex] = choices;
                }

                if (feature.Id == "memory_explorer" || feature.Id == "pc_health_monitor"
                    || feature.Id == "command_history" || feature.Id == "notification_centre")
                {
                    Key action = new Key();
                    action.Font = Type(8f * Zoom);
                    action.Accent = Cyan;
                    action.Text = feature.Id == "memory_explorer" ? "Open memory files"
                        : feature.Id == "pc_health_monitor" ? "Check PC now"
                        : feature.Id == "command_history" ? "View command history" : "View notifications";
                    action.AccessibleName = action.Text;
                    action.Click += delegate
                    {
                        if (!owner.capri.ExperimentalFeatureOn(feature.Id))
                        {
                            MessageBox.Show(this, "Turn this feature on first.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        if (feature.Id == "memory_explorer") owner.capri.OpenMemories();
                        else if (feature.Id == "pc_health_monitor") ShowPcHealth();
                        else ShowActivity(feature.Id == "command_history" ? "Command" : "Reminder",
                            feature.Id == "command_history" ? "Command History" : "Notification Centre");
                    };
                    list.Controls.Add(action);
                    actions[index] = action;
                }
            }

            private void ShowActivity(string kind, string title)
            {
                string content = owner.capri.EnhancementActivityText(kind);
                MessageBox.Show(this, content, "Capri · " + title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            private void ShowPcHealth()
            {
                try
                {
                    PcUse use = Usage.Read();
                    string report = "CPU: " + use.Cpu.ToString("0.0") + "%\n"
                        + "Memory: " + Usage.Size(use.MemoryUsed) + " / " + Usage.Size(use.MemoryTotal) + "\n";
                    if (use.Battery != null) report += "Battery: " + use.Battery + "\n";
                    if (use.Drives.Count > 0) report += "\nDRIVES\n" + string.Join("\n", use.Drives.ToArray());
                    use.Apps.Sort(delegate(AppUse left, AppUse right) { return right.Cpu.CompareTo(left.Cpu); });
                    int count = Math.Min(5, use.Apps.Count);
                    if (count > 0)
                    {
                        report += "\n\nTOP APPS\n";
                        for (int i = 0; i < count; i++)
                            report += use.Apps[i].App + "  " + use.Apps[i].Cpu.ToString("0.0") + "% CPU  " + Usage.Size(use.Apps[i].Memory) + "\n";
                    }
                    MessageBox.Show(this, report, "Capri · PC Health", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception)
                {
                    MessageBox.Show(this, "Couldn't read PC health information just now.", "Capri · PC Health", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            private void LayoutFeatures()
            {
                int y = 8;
                for (int i = 0; i < features.Length; i++)
                {
                    int height = expanded[i] ? 116 : 48;
                    categories[i].Location = new Point(Z(16), Z(y));
                    toggles[i].Location = new Point(Z(16), Z(y + 14));
                    details[i].Location = new Point(Z(570), Z(y + 14));
                    descriptions[i].Location = new Point(Z(28), Z(y + 42));
                    statuses[i].Location = new Point(Z(28), Z(y + 75));
                    descriptions[i].Visible = statuses[i].Visible = expanded[i];
                    details[i].Text = expanded[i] ? "Hide details" : "Details";
                    details[i].Chevron = true;
                    for (int j = 0; j < optionKeys[i].Length; j++)
                    {
                        Key key = optionKeys[i][j];
                        key.Visible = expanded[i];
                        key.Location = new Point(Z(28 + j * 220), Z(y + 94));
                        key.Size = new Size(Z(205), Z(22));
                    }
                    if (actions[i] != null)
                    {
                        actions[i].Visible = expanded[i];
                        actions[i].Enabled = owner.capri.ExperimentalFeatureOn(features[i].Id);
                        actions[i].Location = new Point(Z(470), Z(y + 94));
                        actions[i].Size = new Size(Z(220), Z(22));
                    }
                    y += height;
                }
                list.AutoScrollMinSize = new Size(0, Z(y + 8));
            }

            private void RefreshOptionLabels()
            {
                for (int i = 0; i < features.Length; i++)
                    for (int j = 0; j < features[i].Options.Length; j++)
                    {
                        EnhancementOption option = features[i].Options[j];
                        string value = owner.capri.ExperimentalOption(features[i].Id, option.Id, option.Default);
                        optionKeys[i][j].Text = option.Name + ": " + value;
                    }
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }
            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            private void Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = false;
                label.AutoEllipsis = true;
                label.Bounds = Box(x, y, Wide - x - 28, 20);
                Controls.Add(label);
            }

            internal void Sync()
            {
                syncing = true;
                for (int i = 0; i < features.Length; i++)
                    toggles[i].Checked = owner.capri.ExperimentalFeatureOn(features[i].Id);
                RefreshOptionLabels();
                syncing = false;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2;
                int y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float scale = ScaleOf(this);
                Clip(this, scale);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float scale = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, scale);
                Edging(graphics, this, scale);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    foreach (ContextMenuStrip[] menus in optionMenus)
                        if (menus != null) foreach (ContextMenuStrip menu in menus) if (menu != null) menu.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
