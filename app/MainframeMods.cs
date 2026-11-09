using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private const int ModRows = 3, ModRowTop = 204, ModRowHeight = 62, PersonalRows = 5, PersonalRowTop = 488, PersonalRowHeight = 32;
        private readonly List<KeyValuePair<Control, int>> modLeft = new List<KeyValuePair<Control, int>>();
        private readonly Label[] modNames = new Label[ModRows], modAbouts = new Label[ModRows];
        private readonly Key[] modSwitches = new Key[ModRows], modRemoves = new Key[ModRows], modPublishes = new Key[ModRows];
        private readonly Label[] personalNames = new Label[PersonalRows];
        private readonly Key[] personalSwitches = new Key[PersonalRows];
        private Key personalBack, personalNext;
        private Label personalPage;
        private int personalFirst;
        private readonly string[] personalFeatureIds = {
            "custom_commands", "highlight_assistance", "personal_dashboard",
            "pc_health_monitor", "conversational_continuity", "autonomy", "grudges", "outfit_decider"
        };
        private readonly string[] personalFeatureNames = {
            "Custom commands", "Highlight assistance", "Personal dashboard",
            "PC health monitor", "Conversation continuity", "Autonomy", "Mood and grudges", "Outfit Decider"
        };
        private Key modsKey, modBack, modNext, modBrowse;
        private Head modListHead;
        private readonly Key[] colourKeys = new Key[ArtColors.Slots];
        private Label modStatus, modEmpty, modPage;
        private bool modded;
        private int modFirst;
        private readonly Key[] modTabs = new Key[5], modHides = new Key[ModRows];
        private static readonly string[] ModTabNames = { "All", "Skin", "Code", "Skill", "Mod" };
        private Key modShowHidden;
        private int modKind;
        private bool modShowingHidden;
        private HashSet<string> modHiddenIds;

        private void BuildMods()
        {
            ModAsk = AskToRun;
            int first = Controls.Count;

            Section("MODS >", 0, 58);
            Caption("Code mods can reach your whole PC. Only trusted ones.", 28, 80, Look.Mono(8.5f, FontStyle.Italic, GraphicsUnit.Point), Faint);

            Key import = Make("Import mod", 28, 104, 100, 28, delegate { ImportMod(); });
            import.Accent = Cyan;
            import.Loud = true;
            modBrowse = Make("Browse mods", 134, 104, 100, 28, delegate { BrowseMods(); });
            Key folder = Make("Mods folder", 240, 104, 116, 28, delegate { OpenModsFolder(); });
            Explain(import, "Import mod", "MainframeMods.cs");
            Explain(modBrowse, "Browse mods", "MainframeMods.cs");
            Explain(folder, "Open mods folder", "MainframeMods.cs");

            modStatus = ModLabel("", 28, 138, 200, 36, Font, Faint);
            ((Note)modStatus).Hovers = true;

            modListHead = Section("INSTALLED >", 0, 180);
            int[] tabLeft = { 140, 179, 224, 269, 314 }, tabWide = { 36, 42, 42, 42, 42 };
            for (int i = 0; i < modTabs.Length; i++)
            {
                int kind = i;
                modTabs[i] = Make(ModTabNames[i], tabLeft[i], 178, tabWide[i], 24, delegate { SetModTab(kind); });
                modTabs[i].AccessibleName = kind == 0 ? "Show all mods" : "Show only " + ModTabNames[i].ToLowerInvariant() + " mods";
                Explain(modTabs[i], "Mod filter", "MainframeMods.cs");
            }
            modShowHidden = Make("Show hidden", 236, 142, 120, 24, delegate { ToggleHiddenMods(); });
            Explain(modShowHidden, "Show hidden mods", "MainframeMods.cs");
            modEmpty = ModLabel("No mods yet. Import a " + Mods.Extension + " file.", 28, ModRowTop, 328, 20, Font, Faint);
            for (int i = 0; i < ModRows; i++)
            {
                int row = i, top = ModRowTop + ModRowHeight * i;
                modNames[i] = ModLabel("", 28, top + 3, 196, 20, Type(10f, true), White);
                modSwitches[i] = Make("On", 230, top, 54, 22, delegate { SwitchMod(row); });
                modRemoves[i] = Make("Remove", 290, top, 66, 22, delegate { RemoveMod(row); });
                modAbouts[i] = ModLabel("", 28, top + 26, 196, 30, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
                Explain(modSwitches[i], "Mod switch", "MainframeMods.cs", null, "On / Off");
                Explain(modRemoves[i], "Remove", "MainframeMods.cs");
                modPublishes[i] = Make("Publish", 290, top + 28, 66, 22, delegate { PublishMod(row); });
                Explain(modPublishes[i], "Publish mod", "MainframeMods.cs");
                modHides[i] = Make("Hide", 230, top + 28, 54, 22, delegate { HideMod(row); });
                Explain(modHides[i], "Hide mod", "MainframeMods.cs");
            }
            modBack = Make("Back", 28, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst -= ModRows; FillMods(); });
            modPage = ModLabel("", 96, ModRowTop + ModRowHeight * ModRows + 3, 192, 18, Font, Faint);
            modPage.TextAlign = ContentAlignment.MiddleCenter;
            Explain(modBack, "Mod pages", "MainframeMods.cs");
            modNext = Make("Next", 296, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst += ModRows; FillMods(); });
            Explain(modNext, "Mod pages", "MainframeMods.cs");

            Section("PERSONAL MODS >", 0, 438);
            Key personalConfigure = Make("Configure", 240, 438, 78, 25, delegate { OpenEnhancements(); });
            Explain(personalConfigure, "Configure all personal features", "Enhancements.cs");
            Caption("Your built-in Capri features.", 28, 466, Look.Mono(8.5f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            for (int i = 0; i < PersonalRows; i++)
            {
                int row = i, top = PersonalRowTop + PersonalRowHeight * i;
                personalNames[i] = ModLabel("", 28, top + 3, 190, 22, Type(9.5f, true), White);
                personalSwitches[i] = Make("On", 230, top, 58, 25, delegate { TogglePersonalFeature(personalFirst + row); });
                Explain(personalSwitches[i], "Personal feature", "Enhancements.cs");
            }
            int personalPagerTop = PersonalRowTop + PersonalRowHeight * PersonalRows + 4;
            personalBack = Make("Back", 28, personalPagerTop, 60, 22, delegate { personalFirst -= PersonalRows; FillPersonalMods(); });
            personalPage = ModLabel("", 96, personalPagerTop + 3, 192, 18, Font, Faint);
            personalPage.TextAlign = ContentAlignment.MiddleCenter;
            Explain(personalBack, "Personal feature pages", "MainframeMods.cs");
            personalNext = Make("Next", 296, personalPagerTop, 60, 22, delegate { personalFirst += PersonalRows; FillPersonalMods(); });
            Explain(personalNext, "Personal feature pages", "MainframeMods.cs");

            Section("COLORS >", 0, 688);
            int colourRow = 712;
            for (int i = 0; i < ArtColors.Slots; i++)
            {
                int slot = i;
                colourKeys[i] = Make(ArtColors.Names[i], 28 + i * 66, colourRow, 62, 26, delegate { PickColour(slot); });
                colourKeys[i].AccessibleName = "Change her " + ArtColors.Names[i].ToLowerInvariant() + " color";
                Explain(colourKeys[i], "Her colors", "MainframeMods.cs", null, "Her colors");
            }

            Key colourReset = Make("Reset colors", 28, colourRow + 32, 160, 24, delegate { ResetColours(); });
            Explain(colourReset, "Reset colors", "MainframeMods.cs");

            for (int i = first; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                modLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                control.Left += Far;
                Reachable(control, false);
            }

            FillMods();
        }

        private void TogglePersonalFeature(int index)
        {
            if (index < 0 || index >= personalFeatureIds.Length) return;
            string id = personalFeatureIds[index];
            bool turnOn = !capri.ExperimentalFeatureOn(id);
            try
            {
                if (!capri.SetExperimentalFeature(id, turnOn))
                {
                    ModSays("Couldn't change " + personalFeatureNames[index] + ".", true);
                    FillPersonalMods();
                    return;
                }
                RefreshHighlightHotkey();
                FillPersonalMods();
                ModSays(personalFeatureNames[index] + (turnOn ? " is on." : " is off."), false);
            }
            catch (Exception error)
            {
                ModSays("Couldn't change that feature: " + error.Message, true);
                FillPersonalMods();
            }
        }

        private void FillPersonalMods()
        {
            int total = personalFeatureIds.Length;
            personalFirst = Math.Max(0, Math.Min(personalFirst, (Math.Max(0, total - 1) / PersonalRows) * PersonalRows));
            for (int i = 0; i < PersonalRows; i++)
            {
                int index = personalFirst + i;
                bool shown = index < total;
                personalNames[i].Visible = personalSwitches[i].Visible = shown;
                if (!shown) continue;
                bool on = capri.ExperimentalFeatureOn(personalFeatureIds[index]);
                personalNames[i].Text = personalFeatureNames[index];
                personalSwitches[i].Text = on ? "On" : "Off";
                personalSwitches[i].Accent = on ? Cyan : Rose;
                personalSwitches[i].AccessibleName = (on ? "Turn off " : "Turn on ") + personalFeatureNames[index];
                personalSwitches[i].Invalidate();
            }
            bool pages = total > PersonalRows;
            personalBack.Visible = personalNext.Visible = personalPage.Visible = pages;
            personalBack.Enabled = personalFirst > 0;
            personalNext.Enabled = personalFirst + PersonalRows < total;
            personalPage.Text = "Page " + (personalFirst / PersonalRows + 1) + " of " + ((Math.Max(0, total - 1)) / PersonalRows + 1);
        }
        private Label ModLabel(string text, int x, int y, int width, int height, Font font, Color colour)
        {
            Label label = Caption(text, x, y, font, colour);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Size = new Size(width, height);
            return label;
        }

        internal bool ModsShown { get { return modded; } }

        private void CheckModsKey()
        {
            if (more.Left != Scaled(146) || more.Text != "Settings" || modsKey.Top != more.Top || modsKey.Left < more.Right || modsKey.Right > minimize.Left) throw new Exception("Settings or Mods is in the wrong place.");
            try
            {
                foreach (bool fallback in new[] { false, true })
                {
                    LayoutScale(96, 100, fallback);
                    foreach (bool page in new[] { false, true })
                    {
                        ShowMods(page);
                        string crowded = TextFits() ?? Overlap();
                        if (crowded != null) throw new Exception("The Mods page or key overlaps" + (fallback ? " on the fallback font: " : ": ") + crowded);
                    }
                    ShowSettings(false);
                    if (more.Text != "Back") throw new Exception("Settings lost Back while Mods was open.");
                    more.PerformClick();
                    if (modded || paged || more.Text != "Settings") throw new Exception("Back did not leave Mods.");
                }
            }
            finally { LayoutScale(0, 0); ShowMods(false); }
        }

        internal void ShowMods(bool on)
        {
            DismissHint();
            if (on && paged) ShowSettings(false);
            modded = on;
            more.Text = on ? "Back" : "Settings";
            more.Invalidate();
            Place();
            foreach (KeyValuePair<Control, int> entry in mainLeft) Reachable(entry.Key, !on);
            foreach (KeyValuePair<Control, int> entry in modLeft) Reachable(entry.Key, on);
            if (Hidden(ActiveControl)) more.Focus();
            if (on) FillMods();
            Arrange(expanded);
        }

        private void FillMods()
        {
            List<Mod> mods = ShownMods();
            int total = mods.Count, pageSize = ModRows;
            modFirst = Math.Max(0, Math.Min(modFirst, (Math.Max(0, total - 1) / pageSize) * pageSize));
            modEmpty.Visible = total == 0;
            modEmpty.Text = capri.ModList.All.Count == 0 ? "No mods yet. Import a " + Mods.Extension + " file." : "Nothing here. Try another filter, or Show hidden.";
            for (int i = 0; i < ModRows; i++)
            {
                Mod mod = modFirst + i < total ? mods[modFirst + i] : null;
                modNames[i].Visible = modAbouts[i].Visible = modSwitches[i].Visible = modRemoves[i].Visible = modPublishes[i].Visible = modHides[i].Visible = mod != null;
                if (mod == null) continue;
                modNames[i].Text = mod.File.Name + (string.IsNullOrEmpty(mod.File.Version) ? "" : " v" + mod.File.Version);
                modNames[i].ForeColor = mod.On ? White : Faint;
                bool isHidden = HiddenIds().Contains(mod.File.Id);
                if (isHidden) modNames[i].Text += " (hidden)";
                modHides[i].Text = isHidden ? "Unhide" : "Hide";
                modHides[i].AccessibleName = (isHidden ? "Unhide " : "Hide ") + mod.File.Name + " in this list";
                modHides[i].Invalidate();
                modAbouts[i].Text = ((mod.Changed ? "CHANGED, approve it again. " : mod.Code ? "RUNS CODE. " : mod.Skin ? "SKIN. " : mod.Skill ? "SKILL. " : "")
                    + (string.IsNullOrEmpty(mod.File.Author) ? "" : "by " + mod.File.Author + ". ")
                    + (mod.File.Description ?? "")
                    + (mod.Code || mod.File.WakeNames.Length == 0 ? "" : " Names: " + string.Join(", ", mod.File.WakeNames))
                    + (mod.Skill ? " For " + mod.File.Games[0] + "." : "")).Trim();
                modAbouts[i].ForeColor = mod.Changed ? Rose : Faint;
                modSwitches[i].Text = mod.On ? "On" : "Off";
                modSwitches[i].Accent = mod.On ? Cyan : Rose;
                modSwitches[i].AccessibleName = (mod.On ? "Turn off " : "Turn on ") + mod.File.Name;
                modRemoves[i].AccessibleName = "Remove " + mod.File.Name;
                modPublishes[i].AccessibleName = "Publish " + mod.File.Name + " to the mods directory";
                modSwitches[i].Invalidate();
            }
            bool pages = total > pageSize;
            modBack.Visible = modNext.Visible = modPage.Visible = pages;
            modBack.Enabled = modFirst > 0;
            modNext.Enabled = modFirst + pageSize < total;
            modPage.Text = "Page " + (modFirst / pageSize + 1) + " of " + ((Math.Max(0, total - 1)) / pageSize + 1);

            FillPersonalMods();

            FillModTabs();

            string[] colours = capri.ModList.ColoursNow;
            for (int i = 0; i < ArtColors.Slots; i++)
            {
                string shown = colours[i] ?? ArtColors.Usual[i];
                Color swatch = ColorTranslator.FromHtml(shown);
                colourKeys[i].Accent = swatch.GetBrightness() < 0.2f ? Faint : swatch;
                colourKeys[i].Invalidate();
            }
            if (capri.ModList.Problems.Count > 0) ModSays("Skipped " + capri.ModList.Problems[0], true);
        }

        private HashSet<string> HiddenIds()
        {
            if (modHiddenIds != null) return modHiddenIds;
            modHiddenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string file = Path.Combine(capri.ModList.Folder, "hidden-mods.txt");
                if (File.Exists(file)) foreach (string line in File.ReadAllLines(file)) if (line.Trim().Length > 0) modHiddenIds.Add(line.Trim());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return modHiddenIds;
        }

        private void SaveHiddenIds()
        {
            try
            {
                Directory.CreateDirectory(capri.ModList.Folder);
                string[] ids = new string[HiddenIds().Count];
                HiddenIds().CopyTo(ids);
                File.WriteAllLines(Path.Combine(capri.ModList.Folder, "hidden-mods.txt"), ids, new UTF8Encoding(false));
            }
            catch (IOException error) { ModSays("Couldn't save the hidden list: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't save the hidden list: " + error.Message, true); }
        }

        private static bool ModInTab(Mod mod, int kind)
        {
            bool skin = mod.Skin || mod.File.Colors != null;
            if (kind == 0) return true;
            if (kind == 1) return skin;
            if (kind == 2) return mod.Code;
            if (kind == 3) return mod.Skill;
            return !mod.Code && !mod.Skill && !skin;
        }

        private List<Mod> ShownMods()
        {
            HashSet<string> hidden = HiddenIds();
            List<Mod> shown = new List<Mod>();
            foreach (Mod mod in capri.ModList.All)
                if (ModInTab(mod, modKind) && (modShowingHidden || !hidden.Contains(mod.File.Id))) shown.Add(mod);
            return shown;
        }

        private int CountHidden()
        {
            int count = 0;
            foreach (Mod mod in capri.ModList.All) if (HiddenIds().Contains(mod.File.Id)) count++;
            return count;
        }

        private void FillModTabs()
        {
            for (int i = 0; i < modTabs.Length; i++)
            {
                modTabs[i].Accent = i == modKind ? Cyan : Faint;
                modTabs[i].Invalidate();
            }
            modShowHidden.Text = modShowingHidden ? "Hide them again" : "Show hidden";
            modShowHidden.Accent = modShowingHidden ? Cyan : Faint;
            modShowHidden.AccessibleName = modShowingHidden ? "Stop showing hidden mods" : "Show hidden mods (" + CountHidden() + ")";
            modShowHidden.Invalidate();
        }

        private void SetModTab(int kind)
        {
            modKind = kind;
            modFirst = 0;
            FillMods();
        }

        private void ToggleHiddenMods()
        {
            modShowingHidden = !modShowingHidden;
            modFirst = 0;
            FillMods();
            int count = CountHidden();
            ModSays(!modShowingHidden ? "Hidden mods are out of the list again." : count == 0 ? "No mods are hidden." : "Showing " + count + " hidden mod" + (count == 1 ? "" : "s") + ", marked (hidden).", false);
        }

        private void HideMod(int row)
        {
            HideModAt(modFirst + row);
        }

        private void HideModAt(int index)
        {
            List<Mod> mods = ShownMods();
            if (index < 0 || index >= mods.Count) return;
            Mod mod = mods[index];
            HashSet<string> hidden = HiddenIds();
            bool nowHidden = !hidden.Contains(mod.File.Id);
            if (nowHidden) hidden.Add(mod.File.Id); else hidden.Remove(mod.File.Id);
            SaveHiddenIds();
            FillMods();
            ModSays(nowHidden ? mod.File.Name + " is hidden from this list. It's still installed and working." : mod.File.Name + " is back in the list.", false);
        }

        private bool SwitchModAt(int index)
        {
            List<Mod> mods = ShownMods();
            if (index < 0 || index >= mods.Count) return false;
            Mod mod = mods[index];
            try
            {
                bool done = capri.ModList.Set(mod, !mod.On, ModAsk);
                FillMods();
                ModSays(!done ? mod.File.Name + " stays off. Nothing ran." : mod.File.Name + (mod.On ? " is on." : " is off."), false);
                return done;
            }
            catch (ModException error) { ModSays(error.Message, true); }
            catch (IOException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            return false;
        }

        private void RemoveModAt(int index)
        {
            List<Mod> mods = ShownMods();
            if (index < 0 || index >= mods.Count) return;
            Mod mod = mods[index];
            try
            {
                capri.ModList.Remove(mod);
                if (HiddenIds().Remove(mod.File.Id)) SaveHiddenIds();
                FillMods();
                ModSays(mod.File.Name + " is removed.", false);
            }
            catch (IOException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
        }

        private void PublishModAt(int index)
        {
            List<Mod> mods = ShownMods();
            if (index < 0 || index >= mods.Count) return;
            Mod mod = mods[index];
            PublishSheet sheet = new PublishSheet(this, SheetZoom(PublishSheet.Wide, PublishSheet.Tall), mod);
            try { sheet.ShowDialog(this); }
            finally { sheet.Dispose(); }
        }
        private void ModSays(string text, bool problem)
        {
            modStatus.Text = text;
            modStatus.ForeColor = problem ? Rose : Cyan;
        }

        private void ImportMod()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Import a Capri mod";
                dialog.Filter = "Capri mods (*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension + ";*" + Mods.SkillExtension + ")|*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension + ";*" + Mods.SkillExtension;
                if (dialog.ShowDialog(this) == DialogResult.OK) ImportMod(dialog.FileName);
            }
        }

        internal bool ImportMod(string file)
        {
            try
            {
                Mod mod = capri.ModList.Import(file, ModAsk);
                if (mod == null)
                {
                    ModSays("Not imported. Nothing ran.", false);
                    return false;
                }
                FillMods();
                ModSays(mod.Code ? mod.File.Name + " is on and running." : mod.Skill ? mod.File.Name + " is on. She uses it when she plays " + mod.File.Games[0] + "." : mod.File.WakeNames.Length == 0 ? mod.File.Name + " is on." : mod.File.Name + " is on. She now answers to " + Mods.Listed(mod.File.WakeNames) + " too.", false);
                return true;
            }
            catch (ModException error)
            {
                // Its whole reason goes to errors.txt too, and the list shows a code mod that saved but didn't start as off.
                Brain.Log("mods", -1, Path.GetFileName(file) + ": " + error.Message);
                FillMods();
                ModSays(error.Message, true);
                return false;
            }
        }

        internal bool ModStatusHovers { get { Note note = modStatus as Note; return note != null && note.Hovers; } }

        private static string DirectoryProblem(Exception error, string lead)
        {
            return error is ModException ? error.Message : lead + error.Message;
        }

        private void PickColour(int slot)
        {
            string now = capri.ModList.ColoursNow[slot] ?? ArtColors.Usual[slot];
            using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml(now) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Color picked = dialog.Color;
                try
                {
                    capri.ModList.SetColour(slot, "#" + picked.R.ToString("X2") + picked.G.ToString("X2") + picked.B.ToString("X2"));
                    FillMods();
                    ModSays("Her " + ArtColors.Names[slot].ToLowerInvariant() + " changed. Saved as My Colors, so you can share it.", false);
                }
                catch (IOException error) { ModSays("Couldn't save that color: " + error.Message, true); }
                catch (UnauthorizedAccessException error) { ModSays("Couldn't save that color: " + error.Message, true); }
            }
        }

        private void ResetColours()
        {
            try
            {
                capri.ModList.ResetColours();
                FillMods();
                ModSays("Her own colors are back.", false);
            }
            catch (IOException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
        }

        private bool SwitchMod(int row)
        {
            return SwitchModAt(modFirst + row);
        }
        internal bool SwitchModForTest(int row) { return SwitchModAt(row); }
        internal string ModStatusText { get { return modStatus.Text; } }

        internal Func<ModFile, string, bool> ModAsk;

        private bool AskToRun(ModFile mod, string hash)
        {
            string message = mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version) + (string.IsNullOrEmpty(mod.Author) ? "" : ", which says it's by " + mod.Author) + ", is a code mod.\n\n"
                + "Code mods run as part of Capri with full access to your PC: your files, your accounts and the API keys she has saved. Nothing limits what it can do, and Capri doesn't check whether it's safe.\n\n"
                + "Only run it if you wrote it, or you've read it and trust who did.\n\nFile fingerprint (SHA-256):\n" + hash + "\n\nRun this code?";
            return MessageBox.Show(this, message, "Run this code mod?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void RemoveMod(int row)
        {
            RemoveModAt(modFirst + row);
        }
        private void OpenModsFolder()
        {
            try
            {
                Directory.CreateDirectory(capri.ModList.Folder);
                using (Process.Start("explorer.exe", "\"" + capri.ModList.Folder + "\"")) { }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                ModSays("Couldn't open the mods folder: " + error.Message, true);
            }
        }

        internal void CheckMods(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            Mods mods = capri.ModList;
            Func<string, string, string> sample = delegate(string name, string body)
            {
                string file = Path.Combine(output, name);
                File.WriteAllText(file, body);
                return file;
            };
            string good = sample("custom-names" + Mods.Extension, "{\"format\":1,\"id\":\"custom-names\",\"name\":\"Custom Names\",\"version\":\"1.0.0\",\"author\":\"Avery\",\"description\":\"Answers to more names.\",\"wake_names\":[\"Nova\",\"Big Z\",\"Capricorn\"]}");
            foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
            if (Hands.HeardName("Nova, what time is it?") || !Hands.HeardName("Capri, what time is it?")) throw new Exception("Her names were wrong before any mod.");
            ShowMods(true);
            if (!modded || more.Text != "Back" || modStatus.Left >= Far || modsKey.Left >= Far) throw new Exception("The Mods page did not open.");
            if (!ImportMod(good)) throw new Exception("A good mod was refused: " + modStatus.Text);
            if (mods.All.Count != 1 || !File.Exists(Path.Combine(mods.Folder, "custom-names" + Mods.Extension))) throw new Exception("An imported mod was not saved.");
            if (!Hands.HeardName("hey nova, what time is it") || !Hands.HeardName("BIG Z open notepad") || !Hands.HeardName("big   z") || !Hands.HeardName("ask capricorn") || !Hands.HeardName("Capri, hello"))
                throw new Exception("An imported name was not heard.");
            if (Hands.HeardName("the supernova was bright") || Hands.HeardName("novas are bright") || Hands.HeardName("big zebra"))
                throw new Exception("Part of a word counted as her name.");
            if (Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Big Z, Capricorn or Nova", StringComparison.Ordinal) < 0 && Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Nova, Big Z or Capricorn", StringComparison.Ordinal) < 0)
                throw new Exception("Her brain was not told her nicknames.");
            checks.Add("PASS mods: an imported mod adds whole-word names and tells her brain");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "mods-page.png"), ImageFormat.Png);
            }
            SwitchMod(0);
            if (Hands.HeardName("hey nova") || !Hands.HeardName("hey capri") || Hands.NoteFor(Hands.KitFor()).Contains("They can also call you")) throw new Exception("Turning a mod off left its names on.");
            mods.Load();
            if (mods.All[0].On) throw new Exception("A mod turned off came back on after loading.");
            SwitchMod(0);
            if (!Hands.HeardName("hey nova")) throw new Exception("Turning a mod back on did not bring its names back.");
            checks.Add("PASS mods: on and off apply straight away and off survives a reload");
            string[,] bad =
            {
                { "bad-json", "{nope" },
                { "bad-format", "{\"format\":99,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-path", "{\"format\":1,\"id\":\"../../evil\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-name", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\".*\"]}" },
                { "bad-short", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"x\"]}" },
                { "bad-empty", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[]}" }
            };
            for (int i = 0; i < bad.GetLength(0); i++)
                if (ImportMod(sample(bad[i, 0] + Mods.Extension, bad[i, 1])) || mods.All.Count != 1) throw new Exception("A bad mod was let in: " + bad[i, 0]);
            if (ImportMod(sample("wrong.txt", "{}"))) throw new Exception("A file that isn't a mod was let in.");
            if (Directory.GetFiles(mods.Folder, "*" + Mods.Extension).Length != 1) throw new Exception("A refused mod was left in her folder.");
            checks.Add("PASS mods: broken files, bad ids and bad names are refused");
            RemoveMod(0);
            if (mods.All.Count != 0 || Hands.HeardName("hey nova")) throw new Exception("Removing a mod left it behind.");
            ShowSettings(true);
            if (modded || !paged || modStatus.Left < Far) throw new Exception("Settings did not take over from Mods.");
            ShowMods(true);
            if (paged || !modded || modStatus.Left >= Far) throw new Exception("Mods did not take over from Settings.");
            ShowMods(false);
            if (modded || modStatus.Left < Far) throw new Exception("Back did not leave Mods.");
            CheckModsKey();
            checks.Add("PASS mods key: Mods sits in the CAPRI band, Settings stays where it was and turns into Back on the Mods page, and nothing overlaps with her own font or the fallback");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "main-page.png"), ImageFormat.Png);
            }
            WindowState = wasState;
            checks.Add("PASS mods: removing works and Mods and Settings swap cleanly");
            string ears = sample("ears" + Mods.Extension, "{\"format\":1,\"id\":\"ears\",\"name\":\"Ears\",\"sounds_like\":true,\"heard_as\":[\"Coppering\"],\"wake_names\":[\"Choom\"]}");
            string[] missedBefore = { "Copri, what time is it", "hey coppery", "Yokapri what's up", "CAH Bree are you there", "Shum, what time is it", "hey coppering" };
            string[] stayQuiet = { "copy that file", "the copper ore", "a cobra", "show me the map", "that's a shame", "in the room", "keeper of the keys", "hum a song" };
            foreach (string line in missedBefore) if (Hands.HeardName(line)) throw new Exception("Heard a sound-alike with no mod on: " + line);
            if (!ImportMod(ears)) throw new Exception("A sounds_like mod was refused: " + modStatus.Text);
            foreach (string line in missedBefore) if (!Hands.HeardName(line)) throw new Exception("Sounds like missed: " + line);
            foreach (string line in stayQuiet) if (Hands.HeardName(line)) throw new Exception("Sounds like fired on: " + line);
            if (!Hands.NoteFor(Hands.KitFor()).Contains("They can also call you Choom.")) throw new Exception("Her brain wasn't told only her real nickname.");
            SwitchMod(0);
            if (Hands.HeardName("hey coppery") || !Hands.HeardName("hey capri")) throw new Exception("Turning sounds like off left it on.");
            RemoveMod(0);
            checks.Add("PASS mods: sounds like catches her ears' misspellings, skips everyday words and switches off cleanly");
            string log = sample("log" + Mods.Extension, "{\"format\":1,\"id\":\"log\",\"name\":\"Log\",\"hearing_log\":true}");
            mods.LogHearing("nothing on yet", false);
            if (File.Exists(mods.LogPath)) throw new Exception("She kept a hearing log with no mod asking.");
            if (!ImportMod(log)) throw new Exception("A hearing log mod was refused: " + modStatus.Text);
            mods.LogHearing("hello there", false);
            if (!File.ReadAllText(mods.LogPath).Contains("ignored | hello there")) throw new Exception("The hearing log didn't record a line.");
            RemoveMod(0);
            if (File.Exists(mods.LogPath)) throw new Exception("Removing the hearing log mod left its list behind.");
            checks.Add("PASS mods: the hearing log only runs while its mod is on, and goes with it");
            CheckDirectory(checks, output);
        }
    }
}


