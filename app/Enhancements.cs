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

        internal bool EnhancementForgetRoutine(string phrase)
        {
            return reminders != null && reminders.Routine(new ToolArguments { Action = "forget", Phrase = phrase }).StartsWith("Forgot your routine:", StringComparison.Ordinal);
        }

        internal bool EnhancementCancelReminder(string reminderText)
        {
            return reminders != null && reminders.Remind(new ToolArguments { Action = "cancel", Text = reminderText }).StartsWith("Cancelled your reminder:", StringComparison.Ordinal);
        }

        internal bool ExperimentalFeatureOn(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                bool on;
                if (enhancementFlags.TryGetValue(id, out on)) return on;
                return id == "custom_commands" || id == "better_timers"
                    || id == "memory_explorer" || id == "pc_health_monitor";
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
                remove = new Key(); remove.Text = routines ? "Forget command" : "Cancel reminder";
                remove.Accent = Hot; remove.Bounds = Box(22, 374, 150, 28); remove.Click += delegate { RemoveSelected(); }; Controls.Add(remove);
                Key refresh = new Key(); refresh.Text = "Refresh"; refresh.Bounds = Box(182, 374, 100, 28);
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
                description.Text = valid ? items.Items[items.SelectedIndex].ToString()
                    : Routines ? "Choose a phrase to inspect or remove its saved action." : "Your saved reminders and timers stay on this PC.";
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
                    "Save your own trigger phrases and have Capri run a sequence of actions. Her existing routine engine is the starting point.",
                    "SAVED PHRASES · MULTI-STEP ACTIONS"),
                new Enhancement("immersive_routines", "Immersive Routines", "AUTOMATION",
                    "Let Capri adapt to your day with routines that can respond to time, the apps you open and your activity.",
                    "AUTOMATIC TRIGGERS IN DEVELOPMENT",
                    new EnhancementOption("triggers", "Routine triggers", "Time + apps", "Manual only", "Time only", "Time + apps", "Context-aware")),
                new Enhancement("better_timers", "Better Timers & Reminders", "PRODUCTIVITY",
                    "Manage multiple timers, recurring reminders, snooze and rescheduling. Existing reminders are being extended rather than replaced.",
                    "SAVED TIMERS · REMINDERS · PERSISTENT"),
                new Enhancement("highlight_assistance", "Highlight Assistance", "PRODUCTIVITY",
                    "Select text in an app and ask Capri to explain, summarise, translate or rewrite just that selection instead of analysing your whole screen.",
                    "SELECTION CAPTURE IN DEVELOPMENT",
                    new EnhancementOption("shortcut", "Activation shortcut", "Ctrl + Alt + E", "Ctrl + Alt + E", "Ctrl + Shift + E", "Ctrl + Alt + H")),
                new Enhancement("personal_dashboard", "Personal Dashboard", "PRODUCTIVITY",
                    "A dedicated workspace for your calendar, tasks, reminders and upcoming events. Capri will be able to add and manage events from chat.",
                    "DASHBOARD IN DEVELOPMENT",
                    new EnhancementOption("view", "Default calendar view", "Week", "Day", "Week", "Month"),
                    new EnhancementOption("week_start", "Week starts on", "Monday", "Monday", "Sunday")),
                new Enhancement("memory_explorer", "Memory Explorer", "COMPANION",
                    "Browse, search and manage what Capri remembers, including her notes and older chat transcripts. Nothing is uploaded by this panel.",
                    "LOCAL MEMORY FILES · PRIVATE TO THIS PC"),
                new Enhancement("pc_health_monitor", "PC Health Monitor", "SYSTEM",
                    "See CPU load, memory use, running apps, drive space and battery status. Capri already has a PC usage reader and status tool.",
                    "LIVE CPU · MEMORY · DRIVE · BATTERY READOUT")
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

                if (feature.Id == "custom_commands" || feature.Id == "better_timers" || feature.Id == "memory_explorer" || feature.Id == "pc_health_monitor")
                {
                    Key action = new Key();
                    action.Font = Type(8f * Zoom);
                    action.Accent = Cyan;
                    action.Text = feature.Id == "custom_commands" ? "Manage saved commands" : feature.Id == "better_timers" ? "Manage timers & reminders" : feature.Id == "memory_explorer" ? "Open memory files" : "Check PC now";
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
    }
}
