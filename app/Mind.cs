using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Capri's mind (batch 3a, desktop half). The Discord bot keeps her moods, memories, diary and skills (they live
    // beside the bot) and pushes a snapshot here so the Capri's mind page can show it. Edits the owner makes on the page
    // are queued in a small file and handed to the bot the next time it asks. Nothing here decides anything.
    internal sealed partial class Companion
    {
        private readonly object mindGate = new object();

        internal string MindStatePath() { return AutonomyFile("mind-state.json"); }
        private string MindEditsPath() { return AutonomyFile("mind-edits.txt"); }

        // The bot's snapshot, written whole and swapped in so a reader never sees half a file.
        internal string MindPutBridge(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 700000) return "failed";
            try
            {
                lock (mindGate)
                {
                    string path = MindStatePath(), temporary = path + ".new";
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                }
                return "ok";
            }
            catch (Exception) { return "failed"; }
        }

        // Edits queued from the page, as a JSON array. Handing them over empties the queue.
        internal string MindEditsBridge()
        {
            try
            {
                lock (mindGate)
                {
                    string path = MindEditsPath();
                    if (!File.Exists(path)) return "[]";
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    File.Delete(path);
                    List<string> kept = new List<string>();
                    foreach (string line in lines) if (line.Trim().Length > 0) kept.Add(line.Trim());
                    return "[" + string.Join(",", kept.ToArray()) + "]";
                }
            }
            catch (Exception) { return "[]"; }
        }

        // Used by the Capri's mind page: op is delete, edit or approve.
        internal void MindQueueEdit(string op, string id, string text)
        {
            string line = "{\"op\":\"" + MindJson(op) + "\",\"id\":\"" + MindJson(id) + "\",\"text\":\"" + MindJson(text) + "\"}";
            lock (mindGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MindEditsPath()));
                File.AppendAllText(MindEditsPath(), line + "\n", new UTF8Encoding(false));
            }
        }

        // "on|22|8": emotions on or off, then the quiet hours start and end (-1 for none). Emotions follow the
        // Mood and Grudges switch and its Emotions option. Quiet hours only stop her messaging on her own.
        internal string MindSettingsBridge()
        {
            bool emotions = GrudgesOn() && !string.Equals(ExperimentalOption("grudges", "emotions", "On"), "Off", StringComparison.OrdinalIgnoreCase);
            string quiet = ExperimentalOption("autonomy", "quiet_hours", "Off");
            int start = -1, end = -1;
            if (quiet == "10 pm - 8 am") { start = 22; end = 8; }
            else if (quiet == "11 pm - 7 am") { start = 23; end = 7; }
            else if (quiet == "12 am - 9 am") { start = 0; end = 9; }
            return (emotions ? "on" : "off") + "|" + start.ToString(CultureInfo.InvariantCulture) + "|" + end.ToString(CultureInfo.InvariantCulture);
        }

        private static string MindJson(string value)
        {
            StringBuilder text = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (c == '\\') text.Append("\\\\");
                else if (c == '"') text.Append("\\\"");
                else if (c == '\n') text.Append("\\n");
                else if (c == '\r') text.Append("\\r");
                else if (c == '\t') text.Append("\\t");
                else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            return text.ToString();
        }
    }
}
