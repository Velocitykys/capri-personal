using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Experimental switches live separately from settings.json. A failed write never changes the in-memory value.
    internal sealed partial class Companion
    {
        private readonly object enhancementGate = new object();
        private Dictionary<string, bool> enhancementFlags;

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
                bool existed, previous = enhancementFlags.TryGetValue(id, out existed) && existed;
                enhancementFlags[id] = on;
                if (WriteEnhancementFlags()) return true;
                if (existed) enhancementFlags[id] = previous;
                else enhancementFlags.Remove(id);
                return false;
            }
        }

        internal bool ResetExperimentalFeatures()
        {
            lock (enhancementGate)
            {
                if (enhancementFlags == null) enhancementFlags = ReadEnhancementFlags();
                Dictionary<string, bool> previous = new Dictionary<string, bool>(enhancementFlags);
                enhancementFlags.Clear();
                if (WriteEnhancementFlags()) return true;
                enhancementFlags = previous;
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

        private bool WriteEnhancementFlags()
        {
            string path = settingsPath + ".enhancements.json";
            string temporary = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(Dictionary<string, bool>)).WriteObject(stream, enhancementFlags);
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

        // New experiments are registered here. Unavailable entries remain visible but cannot be switched on until their
        // implementation is wired into Capri, so the panel never pretends a placeholder is working.
        private sealed class Enhancement
        {
            internal readonly string Id, Name, Description;
            internal readonly bool Available;
            internal Enhancement(string id, string name, string description, bool available)
            {
                Id = id; Name = name; Description = description; Available = available;
            }
        }

        private sealed class EnhancementsSheet : Form
        {
            internal const int Wide = 660, Tall = 354;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Enhancement[] features =
            {
                new Enhancement("developer_diagnostics", "Developer diagnostics", "Show the feature system's status and saved-switch count.", true),
                new Enhancement("enhanced_memory", "Enhanced memory", "Not installed yet. Its switch will be enabled when the module is implemented.", false),
                new Enhancement("custom_commands", "Custom commands", "Not installed yet. Its switch will be enabled when the module is implemented.", false)
            };
            private readonly Toggle[] toggles = new Toggle[3];
            private readonly Label diagnostics;
            private bool syncing;

            internal EnhancementsSheet(Mainframe owner, float zoom)
            {
                this.owner = owner;
                Zoom = zoom;
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
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                Caption("Experimental modules for this Capri build. Switches are saved separately from her normal settings.", 28, 52, small, Faint);
                for (int i = 0; i < features.Length; i++) AddFeature(i, small);
                diagnostics = new Note();
                diagnostics.Font = small;
                diagnostics.ForeColor = Cyan;
                diagnostics.BackColor = Ground;
                diagnostics.AutoSize = false;
                diagnostics.Bounds = Box(28, 276, Wide - 56, 18);
                diagnostics.Visible = false;
                Controls.Add(diagnostics);

                Key reset = new Key();
                reset.Text = "Reset switches";
                reset.Font = Type(8.5f * zoom);
                reset.Accent = Hot;
                reset.Bounds = Box(28, Tall - 44, 126, 26);
                reset.Click += delegate
                {
                    if (!owner.capri.ResetExperimentalFeatures())
                    {
                        MessageBox.Show(this, "Couldn't save the reset. Your previous switches have been kept.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    Sync();
                };
                Controls.Add(reset);

                Key done = new Key();
                done.Text = "Done";
                done.Font = Type(9f * zoom, true);
                done.Accent = Cyan;
                done.Loud = true;
                done.Bounds = Box(Wide - 128, Tall - 44, 100, 26);
                done.Click += delegate { Close(); };
                Controls.Add(done);
                Sync();
            }

            private void AddFeature(int row, Font small)
            {
                Enhancement feature = features[row];
                int y = 86 + row * 62;
                Toggle toggle = new Toggle();
                toggle.Text = feature.Name;
                toggle.Font = Type(9.5f * Zoom, true);
                toggle.Switch = 104;
                toggle.Bounds = Box(28, y, Wide - 56, 22);
                toggle.AccessibleName = feature.Name + ". " + feature.Description;
                toggle.Enabled = feature.Available;
                toggle.CheckedChanged += delegate
                {
                    if (syncing || !feature.Available) return;
                    if (!owner.capri.SetExperimentalFeature(feature.Id, toggle.Checked))
                    {
                        MessageBox.Show(this, "Couldn't save that switch. Its previous value has been restored.", "Enhancements", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Sync();
                    }
                    else Sync();
                };
                Controls.Add(toggle);
                toggles[row] = toggle;
                Caption(feature.Description, 28, y + 25, small, feature.Available ? Faint : Faint);
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
                    toggles[i].Checked = features[i].Available && owner.capri.ExperimentalFeatureOn(features[i].Id);
                bool show = owner.capri.ExperimentalFeatureOn("developer_diagnostics");
                int enabled = 0;
                foreach (Enhancement feature in features)
                    if (feature.Available && owner.capri.ExperimentalFeatureOn(feature.Id)) enabled++;
                diagnostics.Text = "EXPERIMENTAL FLAGS  /  " + enabled + " ENABLED  /  STORED SEPARATELY";
                diagnostics.Visible = show;
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
        }
    }
}
