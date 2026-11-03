using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class DashboardTask
    {
        [DataMember] internal string Id;
        [DataMember] internal string Text;
        [DataMember] internal string Due;
        [DataMember] internal bool Done;
        internal DateTime? DueAt
        {
            get { DateTime value; return DateTime.TryParseExact(Due, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out value) ? (DateTime?)value : null; }
        }
        public override string ToString()
        {
            DateTime? due = DueAt;
            return (Done ? "✓  " : "○  ") + Text + (due.HasValue ? "    ·    " + due.Value.ToString("ddd d MMM, HH:mm") : "    ·    No due date");
        }
    }

    // Experimental switches and their options are kept outside settings.json, so an experiment can never damage
    // Capri's established preferences.
    internal sealed partial class Companion
    {
        private readonly object enhancementGate = new object();
        private Dictionary<string, bool> enhancementFlags;
        private Dictionary<string, string> enhancementOptions;

        internal List<Reminder> EnhancementReminders()
        {
            return reminders == null ? new List<Reminder>() : reminders.SnapshotReminders();
        }

        internal List<Routine> EnhancementRoutines()
        {
            return reminders == null ? new List<Routine>() : reminders.SnapshotRoutines();
        }

        internal string EnhancementSaveRoutine(string phrase, string steps)
        {
            return reminders == null ? "Couldn't access saved commands." : reminders.Routine(new ToolArguments { Phrase = phrase, Steps = steps });
        }

        internal string EnhancementSaveReminder(string text, string when)
        {
            return reminders == null ? "Couldn't access reminders." : reminders.Remind(new ToolArguments { Text = text, When = when });
        }

        internal string EnhancementRunRoutine(string phrase)
        {
            return RoutineTool(new ToolArguments { Action = "run", Phrase = phrase });
        }

        internal bool EnhancementForgetRoutine(string phrase)
        {
            return reminders != null && reminders.Routine(new ToolArguments { Action = "forget", Phrase = phrase }).StartsWith("Forgot your routine:", StringComparison.Ordinal);
        }

        internal bool EnhancementCancelReminder(string reminderText)
        {
            return reminders != null && reminders.Remind(new ToolArguments { Action = "cancel", Text = reminderText }).StartsWith("Cancelled your reminder:", StringComparison.Ordinal);
        }

        private readonly object dashboardGate = new object();
        private List<DashboardTask> dashboardTasks;

        private string DashboardFile
        {
            get
            {
                string folder = null;
                try { folder = Path.GetDirectoryName(Brain.MemoriesPath); } catch (Exception) { }
                if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri");
                return Path.Combine(folder, "dashboard.json");
            }
        }

        private List<DashboardTask> ReadDashboardTasks()
        {
            try
            {
                string path = DashboardFile;
                if (!File.Exists(path)) return new List<DashboardTask>();
                using (FileStream stream = File.OpenRead(path))
                {
                    List<DashboardTask> saved = (List<DashboardTask>)new DataContractJsonSerializer(typeof(List<DashboardTask>)).ReadObject(stream);
                    return saved ?? new List<DashboardTask>();
                }
            }
            catch (Exception) { return new List<DashboardTask>(); }
        }

        private bool WriteDashboardTasks(List<DashboardTask> value)
        {
            string path = DashboardFile, temporary = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<DashboardTask>)).WriteObject(stream, value);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return true;
            }
            catch (Exception)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
                return false;
            }
        }

        internal List<DashboardTask> EnhancementTasks()
        {
            lock (dashboardGate)
            {
                if (dashboardTasks == null) dashboardTasks = ReadDashboardTasks();
                List<DashboardTask> copy = new List<DashboardTask>();
                foreach (DashboardTask item in dashboardTasks)
                    if (item != null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Text))
                        copy.Add(new DashboardTask { Id = item.Id, Text = item.Text, Due = item.Due, Done = item.Done });
                copy.Sort(delegate(DashboardTask left, DashboardTask right)
                {
                    DateTime? a = left.DueAt, b = right.DueAt;
                    if (!a.HasValue) return !b.HasValue ? string.Compare(left.Text, right.Text, StringComparison.CurrentCultureIgnoreCase) : 1;
                    if (!b.HasValue) return -1;
                    return a.Value.CompareTo(b.Value);
                });
                return copy;
            }
        }

        internal bool EnhancementAddTask(string text, DateTime? due)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;
            lock (dashboardGate)
            {
                if (dashboardTasks == null) dashboardTasks = ReadDashboardTasks();
                List<DashboardTask> changed = new List<DashboardTask>(dashboardTasks);
                changed.Add(new DashboardTask { Id = Guid.NewGuid().ToString("N"), Text = text, Due = due.HasValue ? due.Value.ToString("o", CultureInfo.InvariantCulture) : "", Done = false });
                if (!WriteDashboardTasks(changed)) return false;
                dashboardTasks = changed;
                return true;
            }
        }

        internal bool EnhancementSetTaskDone(string id, bool done)
        {
            lock (dashboardGate)
            {
                if (dashboardTasks == null) dashboardTasks = ReadDashboardTasks();
                List<DashboardTask> changed = new List<DashboardTask>(dashboardTasks);
                DashboardTask item = changed.Find(task => task.Id == id);
                if (item == null) return false;
                item.Done = done;
                if (!WriteDashboardTasks(changed)) return false;
                dashboardTasks = changed;
                return true;
            }
        }

        internal bool EnhancementDeleteTask(string id)
        {
            lock (dashboardGate)
            {
                if (dashboardTasks == null) dashboardTasks = ReadDashboardTasks();
                List<DashboardTask> changed = new List<DashboardTask>(dashboardTasks);
                DashboardTask item = changed.Find(task => task.Id == id);
                if (item == null) return false;
                changed.Remove(item);
                if (!WriteDashboardTasks(changed)) return false;
                dashboardTasks = changed;
                return true;
            }
        }

        internal bool ExperimentalFeatureOn(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                bool on;
                if (enhancementFlags.TryGetValue(id, out on)) return on;
                return id == "custom_commands" || id == "better_timers" || id == "highlight_assistance"
                    || id == "personal_dashboard" || id == "memory_explorer" || id == "pc_health_monitor";
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
                    if (saved == null) saved = new Dictionary<string, bool>();
                    foreach (string retired in new string[] { "notification_centre", "shared_music_discovery", "milestones", "command_history", "immersive_routines" })
                        saved.Remove(retired);
                    return saved;
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

        private SavedManagerSheet savedManager;

        private void OpenSavedManager(bool routines)
        {
            if (savedManager != null && !savedManager.IsDisposed && savedManager.Zoom != zoom) savedManager.Close();
            if (savedManager == null || savedManager.IsDisposed || savedManager.Routines != routines)
                savedManager = new SavedManagerSheet(this, zoom, routines);
            savedManager.RefreshItems();
            if (!savedManager.Visible) savedManager.Show(this);
            savedManager.Activate();
        }

        private sealed class SavedManagerSheet : Form
        {
            internal readonly float Zoom;
            internal readonly bool Routines;
            private readonly Mainframe owner;
            private readonly ListBox items;
            private readonly Note description;
            private readonly Key remove;
            private readonly Key add;
            private readonly Key run;

            internal SavedManagerSheet(Mainframe owner, float zoom, bool routines)
            {
                this.owner = owner; Zoom = zoom; Routines = routines;
                Text = routines ? "Saved commands" : "Timers and reminders";
                Icon = owner.Icon; ShowInTaskbar = false; TopMost = true;
                FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None; BackColor = Ground; ForeColor = White;
                ClientSize = new Size(Z(520), Z(430));
                Heading(this, routines ? "SAVED COMMANDS" : "TIMERS & REMINDERS", new Point(Z(22), Z(18)), Display(14f * zoom), Rose, 0);
                description = new Note();
                description.Bounds = Box(24, 48, 470, 30); description.Text = routines ? "Choose a phrase to inspect or remove its saved action." : "Your saved reminders and timers stay on this PC.";
                description.ForeColor = Faint; description.BackColor = Ground; description.Font = Type(8.5f * zoom); Controls.Add(description);
                items = new ListBox();
                items.Bounds = Box(22, 88, 476, 270); items.BackColor = Field; items.ForeColor = White;
                items.BorderStyle = BorderStyle.FixedSingle; items.Font = Type(9f * zoom); items.IntegralHeight = false;
                items.SelectedIndexChanged += delegate { UpdateSelection(); }; Controls.Add(items);
                add = new Key(); add.Text = routines ? "New command" : "New reminder"; add.Accent = Cyan;
                add.Bounds = Box(22, 374, 112, 28); add.Click += delegate { AddItem(); }; Controls.Add(add);
                run = new Key(); run.Text = "Run"; run.Bounds = Box(142, 374, 70, 28);
                run.Click += delegate { RunSelected(); }; Controls.Add(run);
                remove = new Key(); remove.Text = routines ? "Forget" : "Cancel"; remove.Accent = Hot;
                remove.Bounds = Box(220, 374, 82, 28); remove.Click += delegate { RemoveSelected(); }; Controls.Add(remove);
                Key refresh = new Key(); refresh.Text = "Refresh"; refresh.Bounds = Box(310, 374, 78, 28);
                refresh.Click += delegate { RefreshItems(); }; Controls.Add(refresh);
                Key close = new Key(); close.Text = "Done"; close.Accent = Cyan; close.Loud = true;
                close.Bounds = Box(398, 374, 100, 28); close.Click += delegate { Close(); }; Controls.Add(close);
                RefreshItems();
            }

            internal void RefreshItems()
            {
                string selected = items.SelectedItem == null ? null : items.SelectedItem.ToString();
                items.Items.Clear();
                if (Routines)
                    foreach (Routine routine in owner.capri.EnhancementRoutines()) items.Items.Add(routine.Phrase + "  —  " + routine.Steps);
                else
                    foreach (Reminder reminder in owner.capri.EnhancementReminders())
                        if (!reminder.Said) items.Items.Add(reminder.Due.ToString("ddd, d MMM  HH:mm") + "  —  " + reminder.Text);
                if (items.Items.Count == 0) items.Items.Add(Routines ? "No saved commands yet." : "No upcoming reminders.");
                for (int i = 0; i < items.Items.Count; i++)
                    if (selected != null && string.Equals(items.Items[i].ToString(), selected, StringComparison.Ordinal)) { items.SelectedIndex = i; break; }
                UpdateSelection();
            }

            private void UpdateSelection()
            {
                bool valid = items.SelectedIndex >= 0 && items.Items.Count > 0
                    && !items.Items[items.SelectedIndex].ToString().StartsWith("No saved", StringComparison.Ordinal)
                    && !items.Items[items.SelectedIndex].ToString().StartsWith("No upcoming", StringComparison.Ordinal);
                remove.Enabled = valid;
                run.Enabled = valid && Routines;
                description.Text = valid ? items.Items[items.SelectedIndex].ToString()
                    : Routines ? "Create a command Capri can run when you say its trigger phrase." : "Add a reminder using a time like in 20 minutes or tomorrow at 9.";
            }

            private void AddItem()
            {
                using (SavedItemEditorSheet editor = new SavedItemEditorSheet(Zoom, Routines))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    string result = Routines ? owner.capri.EnhancementSaveRoutine(editor.First, editor.Second)
                        : owner.capri.EnhancementSaveReminder(editor.First, editor.Second);
                    MessageBox.Show(this, result, Routines ? "Saved command" : "Reminder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshItems();
                }
            }

            private void RunSelected()
            {
                if (!Routines || items.SelectedIndex < 0) return;
                string selected = items.SelectedItem.ToString();
                if (selected.StartsWith("No saved", StringComparison.Ordinal)) return;
                string phrase = selected.Split(new string[] { "  —  " }, StringSplitOptions.None)[0];
                string result = owner.capri.EnhancementRunRoutine(phrase);
                MessageBox.Show(this, result, "Run command", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshItems();
            }

            private void RemoveSelected()
            {
                if (items.SelectedIndex < 0) return;
                string selected = items.SelectedItem.ToString();
                if (selected.StartsWith("No saved", StringComparison.Ordinal) || selected.StartsWith("No upcoming", StringComparison.Ordinal)) return;
                bool removed;
                if (Routines)
                {
                    string phrase = selected.Split(new string[] { "  —  " }, StringSplitOptions.None)[0];
                    removed = owner.capri.EnhancementForgetRoutine(phrase);
                }
                else
                {
                    string value = selected.Substring(selected.IndexOf("  —  ", StringComparison.Ordinal) + 5);
                    removed = owner.capri.EnhancementCancelReminder(value);
                }
                if (!removed) MessageBox.Show(this, "Capri couldn't remove that item. Refresh and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshItems();
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
                    "Save a trigger phrase and the sequence of actions Capri should run when you say it. Create, run and forget commands here.",
                    "SAVED PHRASES · MULTI-STEP ACTIONS"),
                new Enhancement("better_timers", "Better Timers & Reminders", "PRODUCTIVITY",
                    "Create, review and cancel multiple timers and reminders. They are saved locally and can fire while Capri is running.",
                    "SAVED TIMERS · REMINDERS · PERSISTENT"),
                new Enhancement("highlight_assistance", "Highlight Assistance", "PRODUCTIVITY",
                    "Select text anywhere, press Ctrl + Alt + E, then ask Capri to explain, summarise, rewrite or translate just that selection.",
                    "GLOBAL SHORTCUT · SELECTED TEXT → CAPRI"),
                new Enhancement("personal_dashboard", "Personal Dashboard", "PRODUCTIVITY",
                    "A local calendar and task workspace with due dates, completion tracking and your existing reminders together.",
                    "LOCAL TASKS · CALENDAR · REMINDERS"),
                new Enhancement("memory_explorer", "Memory Explorer", "COMPANION",
                    "Open Capri's local memories file in your text editor so you can review or edit what she remembers. Nothing is uploaded.",
                    "LOCAL MEMORY FILES · PRIVATE TO THIS PC"),
                new Enhancement("pc_health_monitor", "PC Health Monitor", "SYSTEM",
                    "Check current CPU load, memory use, running apps, drive space and battery status using Capri's local PC usage reader.",
                    "ON-DEMAND READOUT · NO BACKGROUND MONITORING")
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
                toggles[index].Enabled = true;
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

                    ContextMenuStrip choices = owner.Dropdown();
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

                if (feature.Id == "custom_commands" || feature.Id == "better_timers" || feature.Id == "highlight_assistance" || feature.Id == "personal_dashboard" || feature.Id == "memory_explorer" || feature.Id == "pc_health_monitor")
                {
                    Key action = new Key();
                    action.Font = Type(8f * Zoom);
                    action.Accent = Cyan;
                    action.Text = feature.Id == "custom_commands" ? "Manage saved commands" : feature.Id == "better_timers" ? "Manage timers & reminders" : feature.Id == "highlight_assistance" ? "Open text assistant" : feature.Id == "personal_dashboard" ? "Open dashboard" : feature.Id == "memory_explorer" ? "Open memory files" : "Check PC now";
                    action.AccessibleName = action.Text;
                    action.Click += delegate
                    {
                        if (!owner.capri.ExperimentalFeatureOn(feature.Id))
                        {
                            MessageBox.Show(this, "Turn this feature on first.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        if (feature.Id == "custom_commands") owner.OpenSavedManager(true);
                        else if (feature.Id == "better_timers") owner.OpenSavedManager(false);
                        else if (feature.Id == "highlight_assistance") owner.OpenHighlightAssistance("");
                        else if (feature.Id == "personal_dashboard") owner.OpenDashboard();
                        else if (feature.Id == "memory_explorer") owner.capri.OpenMemories();
                        else ShowPcHealth();
                    };
                    list.Controls.Add(action);
                    actions[index] = action;
                }
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

        private sealed class HighlightAssistanceSheet : Form
        {
            private readonly Mainframe owner;
            private readonly float scale;
            private readonly TextBox selection;
            private readonly TextBox instruction;
            internal HighlightAssistanceSheet(Mainframe owner, float zoom, string selected)
            {
                this.owner = owner; scale = zoom; Text = "Highlight Assistance"; Icon = owner.Icon;
                ShowInTaskbar = false; TopMost = true; FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
                BackColor = Ground; ForeColor = White; ClientSize = new Size(Z(600), Z(480));
                Label title = LabelText("HIGHLIGHT ASSISTANCE", 22, 18, 550, 24, Cyan, 12f, true); Controls.Add(title);
                Label hint = LabelText("Ctrl + Alt + E captures selected text from the active app.", 22, 48, 550, 20, Faint, 8.5f, false); Controls.Add(hint);
                Label source = LabelText("SELECTED TEXT", 22, 82, 550, 18, Rose, 8f, true); Controls.Add(source);
                selection = new TextBox(); selection.Multiline = true; selection.ScrollBars = ScrollBars.Vertical;
                selection.AcceptsReturn = true; selection.Bounds = Box(22, 104, 556, 190);
                selection.BackColor = Field; selection.ForeColor = White; selection.BorderStyle = BorderStyle.FixedSingle;
                selection.Font = Type(9f * scale); selection.Text = selected ?? ""; Controls.Add(selection);
                Label ask = LabelText("WHAT SHOULD CAPRI DO WITH IT?", 22, 308, 550, 18, Rose, 8f, true); Controls.Add(ask);
                instruction = new TextBox(); instruction.Bounds = Box(22, 330, 556, 34); instruction.BackColor = Field;
                instruction.ForeColor = White; instruction.BorderStyle = BorderStyle.FixedSingle; instruction.Font = Type(9f * scale);
                instruction.Text = "Explain this clearly and simply."; Controls.Add(instruction);
                string[] actions = { "Explain", "Summarise", "Rewrite", "Translate" };
                for (int i = 0; i < actions.Length; i++)
                {
                    string action = actions[i]; Key button = new Key(); button.Text = action; button.Font = Type(8f * scale);
                    button.Accent = Cyan; button.Bounds = Box(22 + i * 112, 382, 102, 30);
                    button.Click += delegate { Send(action); }; Controls.Add(button);
                }
                Key custom = new Key(); custom.Text = "Ask Capri"; custom.Loud = true; custom.Accent = Rose;
                custom.Bounds = Box(22, 426, 130, 28); custom.Click += delegate { Send(instruction.Text); }; Controls.Add(custom);
                Key close = new Key(); close.Text = "Close"; close.Bounds = Box(478, 426, 100, 28);
                close.Click += delegate { Close(); }; Controls.Add(close);
                if (string.IsNullOrWhiteSpace(selected)) hint.Text = "No text was captured. Paste or type text below, then choose an action.";
            }
            private void Send(string action)
            {
                string text = selection.Text.Trim();
                if (text.Length == 0) { MessageBox.Show(this, "Select or paste some text first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                string request = "For the following selected text, " + action.Trim() + ". Keep your response focused on the selection and follow any extra instruction: " + instruction.Text.Trim() + "\n\nSELECTED TEXT:\n" + text;
                owner.capri.Hear(request);
                Close();
            }
            private Label LabelText(string text, int x, int y, int w, int h, Color colour, float size, bool bold)
            {
                return new Label { Text = text, Bounds = Box(x, y, w, h), ForeColor = colour, BackColor = Ground, Font = Look.Mono(size * scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point), AutoEllipsis = true };
            }
            private int Z(int value) { return (int)Math.Round(value * scale); }
            private Rectangle Box(int x, int y, int w, int h) { return new Rectangle(Z(x), Z(y), Z(w), Z(h)); }
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e); Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }
            protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); float s = ScaleOf(this); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Backdrop(e.Graphics, this, s); Edging(e.Graphics, this, s); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; } base.OnKeyDown(e); }
        }

        private sealed class DashboardSheet : Form
        {
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly MonthCalendar calendar;
            private readonly ListBox tasks;
            private readonly ListBox reminders;
            private readonly Label summary;
            private List<DashboardTask> shownTasks = new List<DashboardTask>();
            private List<Reminder> shownReminders = new List<Reminder>();
            private bool allTasksShown;
            internal DashboardSheet(Mainframe owner, float zoom)
            {
                this.owner = owner; Zoom = zoom; Text = "Personal Dashboard"; Icon = owner.Icon;
                ShowInTaskbar = false; TopMost = true; FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
                BackColor = Ground; ForeColor = White; ClientSize = new Size(Z(760), Z(620));
                Label title = Head("PERSONAL DASHBOARD", 22, 18, 15f, Cyan); Controls.Add(title);
                summary = new Note(); summary.Text = ""; summary.Bounds = Box(24, 48, 710, 22);
                summary.ForeColor = Faint; summary.BackColor = Ground; summary.Font = Type(8.5f * scale); Controls.Add(summary);
                Label day = Head("CALENDAR", 22, 82, 8f, Rose); Controls.Add(day);
                calendar = new MonthCalendar(); calendar.Bounds = Box(22, 108, 230, 190);
                calendar.MaxSelectionCount = 1; calendar.DateSelected += delegate { RefreshDashboard(); }; Controls.Add(calendar);
                Label taskHead = Head("TASKS", 278, 82, 8f, Rose); Controls.Add(taskHead);
                tasks = new ListBox(); tasks.Bounds = Box(278, 108, 456, 190); tasks.BackColor = Field;
                tasks.ForeColor = White; tasks.BorderStyle = BorderStyle.FixedSingle; tasks.Font = Type(8.5f * scale);
                tasks.IntegralHeight = false; Controls.Add(tasks);
                Key add = new Key(); add.Text = "Add task"; add.Accent = Cyan; add.Bounds = Box(278, 310, 104, 28); add.Click += delegate { AddTask(); }; Controls.Add(add);
                Key done = new Key(); done.Text = "Complete / undo"; done.Bounds = Box(392, 310, 132, 28); done.Click += delegate { ToggleTask(); }; Controls.Add(done);
                Key delete = new Key(); delete.Text = "Delete"; delete.Accent = Hot; delete.Bounds = Box(534, 310, 90, 28); delete.Click += delegate { DeleteTask(); }; Controls.Add(delete);
                Key all = new Key(); all.Text = "Show all"; all.Bounds = Box(634, 310, 100, 28); all.Click += delegate { calendar.SetDate(DateTime.Today); RefreshDashboard(true); }; Controls.Add(all);
                Label reminderHead = Head("UPCOMING REMINDERS", 22, 366, 8f, Rose); Controls.Add(reminderHead);
                reminders = new ListBox(); reminders.Bounds = Box(22, 392, 712, 154); reminders.BackColor = Field;
                reminders.ForeColor = White; reminders.BorderStyle = BorderStyle.FixedSingle; reminders.Font = Type(8.5f * scale);
                reminders.IntegralHeight = false; Controls.Add(reminders);
                Key refresh = new Key(); refresh.Text = "Refresh"; refresh.Bounds = Box(22, 562, 100, 28); refresh.Click += delegate { RefreshDashboard(); }; Controls.Add(refresh);
                Key close = new Key(); close.Text = "Done"; close.Accent = Cyan; close.Loud = true;
                close.Bounds = Box(634, 562, 100, 28); close.Click += delegate { Close(); }; Controls.Add(close);
                RefreshDashboard();
            }
            private float scale { get { return Zoom; } }
            internal void RefreshDashboard(bool allTasks = false)
            {
                allTasksShown = allTasks;
                shownTasks = owner.capri.EnhancementTasks();
                tasks.Items.Clear();
                DateTime selected = calendar.SelectionStart.Date;
                List<DashboardTask> visible = shownTasks.FindAll(item => allTasks || !item.DueAt.HasValue || item.DueAt.Value.Date == selected || (!item.Done && item.DueAt.Value.Date < DateTime.Today));
                int done = shownTasks.FindAll(item => item.Done).Count;
                summary.Text = DateTime.Today.ToString("dddd, d MMMM yyyy") + "   ·   " + done + " / " + shownTasks.Count + " tasks complete   ·   " + (shownTasks.Count - done) + " remaining";
                foreach (DashboardTask item in visible) tasks.Items.Add(item.ToString());
                if (visible.Count == 0) tasks.Items.Add("No tasks for this day. Add one when you're ready.");
                shownReminders = owner.capri.EnhancementReminders().FindAll(item => !item.Said);
                reminders.Items.Clear();
                foreach (Reminder item in shownReminders) reminders.Items.Add(item.Due.ToString("ddd d MMM  ·  HH:mm") + "    " + item.Text);
                if (shownReminders.Count == 0) reminders.Items.Add("No upcoming reminders.");
            }
            private void AddTask()
            {
                using (DashboardTaskEditor editor = new DashboardTaskEditor(Zoom))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) return;
                    if (!owner.capri.EnhancementAddTask(editor.TaskText, editor.HasDue ? (DateTime?)editor.Due : null))
                        MessageBox.Show(this, "Couldn't save that task. Check the local dashboard file permissions.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshDashboard();
                }
            }
            private DashboardTask SelectedTask()
            {
                int index = tasks.SelectedIndex;
                if (index < 0 || index >= shownTasks.Count) return null;
                DateTime selected = calendar.SelectionStart.Date;
                List<DashboardTask> visible = shownTasks.FindAll(item => allTasksShown || !item.DueAt.HasValue || item.DueAt.Value.Date == selected || (!item.Done && item.DueAt.Value.Date < DateTime.Today));
                return index < visible.Count ? visible[index] : null;
            }
            private void ToggleTask() { DashboardTask item = SelectedTask(); if (item == null) return; owner.capri.EnhancementSetTaskDone(item.Id, !item.Done); RefreshDashboard(); }
            private void DeleteTask() { DashboardTask item = SelectedTask(); if (item == null) return; if (MessageBox.Show(this, "Delete “" + item.Text + "”?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { owner.capri.EnhancementDeleteTask(item.Id); RefreshDashboard(); } }
            private Label Head(string text, int x, int y, float size, Color colour) { return new Label { Text = text, Bounds = Box(x, y, 500, 18), ForeColor = colour, BackColor = Ground, Font = Look.Mono(size * scale, FontStyle.Bold, GraphicsUnit.Point) }; }
            private int Z(int value) { return (int)Math.Round(value * scale); }
            private Rectangle Box(int x, int y, int w, int h) { return new Rectangle(Z(x), Z(y), Z(w), Z(h)); }
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e); Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }
            protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); float s = ScaleOf(this); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Backdrop(e.Graphics, this, s); Edging(e.Graphics, this, s); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; } base.OnKeyDown(e); }
        }

        private sealed class DashboardTaskEditor : Form
        {
            private readonly float scale;
            private readonly TextBox task;
            private readonly CheckBox useDue;
            private readonly DateTimePicker due;
            internal string TaskText { get { return task.Text.Trim(); } }
            internal bool HasDue { get { return useDue.Checked; } }
            internal DateTime Due { get { return due.Value; } }
            internal DashboardTaskEditor(float zoom)
            {
                scale = zoom; Text = "Add task"; FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
                BackColor = Ground; ForeColor = White; ClientSize = new Size(Z(420), Z(190));
                Label label = new Label { Text = "TASK", Bounds = Box(18, 16, 380, 18), ForeColor = Cyan, BackColor = Ground, Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point) }; Controls.Add(label);
                task = new TextBox(); task.Bounds = Box(18, 38, 384, 28); task.BackColor = Field; task.ForeColor = White; task.BorderStyle = BorderStyle.FixedSingle; task.Font = Type(9f * zoom); Controls.Add(task);
                useDue = new CheckBox(); useDue.Text = "Add a due date"; useDue.Bounds = Box(18, 78, 130, 24); useDue.ForeColor = White; useDue.BackColor = Ground; Controls.Add(useDue);
                due = new DateTimePicker(); due.Format = DateTimePickerFormat.Custom; due.CustomFormat = "ddd, d MMM yyyy  HH:mm"; due.Bounds = Box(158, 78, 244, 24); due.Value = DateTime.Now.AddHours(1); Controls.Add(due);
                useDue.CheckedChanged += delegate { due.Enabled = useDue.Checked; }; due.Enabled = false;
                Button save = new Button { Text = "SAVE", DialogResult = DialogResult.OK, Bounds = Box(302, 138, 100, 28), Enabled = false, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Cyan, Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point) };
                save.FlatAppearance.BorderColor = Edge; task.TextChanged += delegate { save.Enabled = TaskText.Length > 0; }; Controls.Add(save); AcceptButton = save;
                Button cancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, Bounds = Box(192, 138, 100, 28), FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = White, Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point) };
                cancel.FlatAppearance.BorderColor = Edge; Controls.Add(cancel); CancelButton = cancel;
            }
            private int Z(int value) { return (int)Math.Round(value * scale); }
            private Rectangle Box(int x, int y, int w, int h) { return new Rectangle(Z(x), Z(y), Z(w), Z(h)); }
        }

        private sealed class SavedItemEditorSheet : Form
        {
            internal string First { get { return first.Text.Trim(); } }
            internal string Second { get { return second.Text.Trim(); } }
            private readonly TextBox first;
            private readonly TextBox second;
            private readonly bool routines;
            private readonly float scale;

            internal SavedItemEditorSheet(float zoom, bool routines)
            {
                this.routines = routines; scale = zoom;
                Text = routines ? "New command" : "New reminder";
                FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; BackColor = Ground; ForeColor = White;
                ClientSize = new Size(Z(440), Z(routines ? 260 : 210));
                Label heading = new Note(); heading.Text = routines ? "TRIGGER PHRASE" : "REMIND ME ABOUT";
                heading.ForeColor = Cyan; heading.BackColor = Ground; heading.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                heading.Bounds = Box(18, 16, 400, 18); Controls.Add(heading);
                first = new TextBox(); first.Bounds = Box(18, 38, 404, 28); first.Font = Type(9f * zoom);
                first.BackColor = Field; first.ForeColor = White; first.BorderStyle = BorderStyle.FixedSingle; Controls.Add(first);
                Label secondLabel = new Note(); secondLabel.Text = routines ? "WHAT SHOULD CAPRI DO?" : "WHEN?  (e.g. IN 20 MINUTES / TOMORROW AT 9)";
                secondLabel.ForeColor = Cyan; secondLabel.BackColor = Ground; secondLabel.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                secondLabel.Bounds = Box(18, 78, 404, 18); Controls.Add(secondLabel);
                if (routines)
                {
                    second = new TextBox(); second.Multiline = true; second.ScrollBars = ScrollBars.Vertical;
                    second.Bounds = Box(18, 100, 404, 92); second.Font = Type(9f * zoom); second.BackColor = Field;
                    second.ForeColor = White; second.BorderStyle = BorderStyle.FixedSingle; Controls.Add(second);
                }
                else
                {
                    second = new TextBox(); second.Bounds = Box(18, 100, 404, 28); second.Font = Type(9f * zoom);
                    second.BackColor = Field; second.ForeColor = White; second.BorderStyle = BorderStyle.FixedSingle; Controls.Add(second);
                }
                Button save = new Button(); save.Text = "SAVE"; save.DialogResult = DialogResult.OK; save.Bounds = Box(322, routines ? 210 : 156, 100, 28);
                save.FlatStyle = FlatStyle.Flat; save.BackColor = Field; save.ForeColor = Cyan; save.FlatAppearance.BorderColor = Edge;
                save.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                save.Enabled = false; first.TextChanged += delegate { save.Enabled = First.Length > 0 && Second.Length > 0; };
                second.TextChanged += delegate { save.Enabled = First.Length > 0 && Second.Length > 0; };
                Controls.Add(save); AcceptButton = save;
                Button cancel = new Button(); cancel.Text = "CANCEL"; cancel.DialogResult = DialogResult.Cancel;
                cancel.FlatStyle = FlatStyle.Flat; cancel.BackColor = Field; cancel.ForeColor = White; cancel.FlatAppearance.BorderColor = Edge;
                cancel.Font = Look.Mono(8f * zoom, FontStyle.Bold, GraphicsUnit.Point);
                cancel.Bounds = Box(212, routines ? 210 : 156, 100, 28); Controls.Add(cancel); CancelButton = cancel;
            }
            private int Z(int value) { return (int)Math.Round(value * scale); }
            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }
        }
    }
}
