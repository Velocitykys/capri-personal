using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CapriDesktop
{
    internal enum AutonomyMode { Off, Ask, Free }
    internal enum AutonomyVerdict { Allowed, Confirm, Denied }

    internal sealed class AutonomyDecision
    {
        internal AutonomyVerdict Verdict;
        internal string Category = "";
        internal string Reason = "";
        internal int Used;
        internal int Limit;
    }

    [DataContract]
    internal sealed class AutonomyBudget
    {
        [DataMember(Name = "day")] public string Day;
        [DataMember(Name = "used")] public int Used;
    }

    // The owner's rules for what Capri may do on her own (independence work, batch 1).
    // Everything autonomous asks AutonomyAuthorize first. Switched off, or with an area set to Off, the answer is no.
    // Ask first means she needs the owner's OK; Free means she may go ahead. Every allowed action spends from a daily
    // budget, and every decision is written to autonomy-log.txt beside her settings. Nothing here changes what she does
    // when the owner asks for something directly.
    internal sealed partial class Companion
    {
        internal static readonly string[] AutonomyCategories = { "messaging", "research", "learning", "self_edits", "pc_actions" };
        private const int AutonomyLogLimit = 200000;
        private readonly object autonomyGate = new object();
        private AutonomyBudget autonomyBudget;

        internal static string AutonomyLabel(string category)
        {
            switch (category)
            {
                case "messaging": return "Messaging";
                case "research": return "Research";
                case "learning": return "Learning";
                case "self_edits": return "Self-edits";
                case "pc_actions": return "PC actions";
                default: return category ?? "";
            }
        }

        internal static string AutonomyModeName(AutonomyMode mode)
        {
            return mode == AutonomyMode.Free ? "Free" : mode == AutonomyMode.Ask ? "Ask first" : "Off";
        }

        // What the owner chose for one area. Off unless the master switch is on and the area was set.
        internal AutonomyMode AutonomyModeFor(string category)
        {
            if (Array.IndexOf(AutonomyCategories, category ?? "") < 0) return AutonomyMode.Off;
            if (!ExperimentalFeatureOn("autonomy")) return AutonomyMode.Off;
            string value = ExperimentalOption("autonomy", category, "Off");
            if (string.Equals(value, "Free", StringComparison.OrdinalIgnoreCase)) return AutonomyMode.Free;
            if (string.Equals(value, "Ask first", StringComparison.OrdinalIgnoreCase)) return AutonomyMode.Ask;
            return AutonomyMode.Off;
        }

        internal int AutonomyDailyLimit()
        {
            int limit;
            if (!int.TryParse(ExperimentalOption("autonomy", "daily_budget", "100"), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)) limit = 100;
            return Math.Max(1, Math.Min(100000, limit));
        }

        private string AutonomyFile(string name)
        {
            return Path.Combine(Path.GetDirectoryName(settingsPath), name);
        }

        private static string AutonomyToday()
        {
            return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Call with autonomyGate held. The count starts again each calendar day.
        private AutonomyBudget AutonomyLoadBudget()
        {
            if (autonomyBudget == null)
            {
                autonomyBudget = new AutonomyBudget();
                try
                {
                    string path = AutonomyFile("autonomy-budget.json");
                    if (File.Exists(path))
                        using (FileStream stream = File.OpenRead(path))
                        {
                            AutonomyBudget saved = (AutonomyBudget)new DataContractJsonSerializer(typeof(AutonomyBudget)).ReadObject(stream);
                            if (saved != null) autonomyBudget = saved;
                        }
                }
                catch (Exception) { autonomyBudget = new AutonomyBudget(); }
            }
            string today = AutonomyToday();
            if (autonomyBudget.Day != today) { autonomyBudget.Day = today; autonomyBudget.Used = 0; }
            if (autonomyBudget.Used < 0) autonomyBudget.Used = 0;
            return autonomyBudget;
        }

        private void AutonomySaveBudget()
        {
            string path = AutonomyFile("autonomy-budget.json"), temporary = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(AutonomyBudget)).WriteObject(stream, autonomyBudget);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (Exception)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
            }
        }

        // Call with autonomyGate held. One line per decision; the file is trimmed to its newest lines when it grows.
        private void AutonomyLog(string category, string verdict, string what)
        {
            try
            {
                string clean = (what ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (clean.Length > 200) clean = clean.Substring(0, 200);
                string path = AutonomyFile("autonomy-log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " | " + (category ?? "") + " | " + verdict + " | " + clean + Environment.NewLine;
                File.AppendAllText(path, line, new UTF8Encoding(false));
                FileInfo info = new FileInfo(path);
                if (info.Length > AutonomyLogLimit)
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    int keep = Math.Min(lines.Length, 500);
                    string[] newest = new string[keep];
                    Array.Copy(lines, lines.Length - keep, newest, 0, keep);
                    File.WriteAllLines(path, newest, new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }

        // Asks the owner's rules whether Capri may do this on her own. An allowed answer spends cost from today's budget.
        // confirmed is true only after the owner said yes to an Ask first question.
        internal AutonomyDecision AutonomyAuthorize(string category, string what, int cost, bool confirmed)
        {
            if (cost < 1) cost = 1;
            category = (category ?? "").Trim().ToLowerInvariant();
            AutonomyDecision result = new AutonomyDecision();
            result.Category = category;
            lock (autonomyGate)
            {
                AutonomyBudget budget = AutonomyLoadBudget();
                result.Limit = AutonomyDailyLimit();
                result.Used = budget.Used;
                AutonomyMode mode = AutonomyModeFor(category);
                if (Array.IndexOf(AutonomyCategories, category) < 0)
                {
                    result.Verdict = AutonomyVerdict.Denied;
                    result.Reason = "Unknown autonomy area.";
                }
                else if (!ExperimentalFeatureOn("autonomy"))
                {
                    result.Verdict = AutonomyVerdict.Denied;
                    result.Reason = "Autonomy is switched off.";
                }
                else if (mode == AutonomyMode.Off)
                {
                    result.Verdict = AutonomyVerdict.Denied;
                    result.Reason = AutonomyLabel(category) + " is set to Off.";
                }
                else if (budget.Used + cost > result.Limit)
                {
                    result.Verdict = AutonomyVerdict.Denied;
                    result.Reason = "Today's autonomy budget is used up (" + budget.Used + " of " + result.Limit + ").";
                }
                else if (mode == AutonomyMode.Ask && !confirmed)
                {
                    result.Verdict = AutonomyVerdict.Confirm;
                    result.Reason = AutonomyLabel(category) + " needs the owner's OK first.";
                }
                else
                {
                    result.Verdict = AutonomyVerdict.Allowed;
                    result.Reason = mode == AutonomyMode.Free ? "Allowed." : "Allowed after the owner said yes.";
                    budget.Used += cost;
                    result.Used = budget.Used;
                    AutonomySaveBudget();
                }
                AutonomyLog(category, result.Verdict.ToString().ToUpperInvariant() + (confirmed ? " (confirmed)" : ""), what);
            }
            return result;
        }

        // A short readout for the owner: the master switch, each area, and today's budget.
        internal string AutonomyStatusText()
        {
            bool on = ExperimentalFeatureOn("autonomy");
            StringBuilder text = new StringBuilder();
            text.Append("Autonomy is ").Append(on ? "ON" : "OFF").Append(".\n");
            foreach (string category in AutonomyCategories)
                text.Append(AutonomyLabel(category)).Append(": ").Append(AutonomyModeName(AutonomyModeFor(category))).Append("\n");
            int used, limit;
            lock (autonomyGate) { used = AutonomyLoadBudget().Used; limit = AutonomyDailyLimit(); }
            text.Append("Today's budget: ").Append(used).Append(" of ").Append(limit).Append(" used.");
            return text.ToString();
        }

        // The Discord bridge's door into the rules. Answers are "verdict|reason|used/limit", so the bot can fail closed.
        // Anything it doesn't understand is a no.
        internal string AutonomyBridge(string action, string category, string what)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            if (action == "status") return AutonomyStatusText();
            if (action == "check" || action == "confirm")
            {
                AutonomyDecision decision = AutonomyAuthorize(category, what, 1, action == "confirm");
                return decision.Verdict.ToString().ToLowerInvariant() + "|" + decision.Reason + "|" + decision.Used + "/" + decision.Limit;
            }
            return "denied|Unknown autonomy request.|0/0";
        }

        // Her grudge after being thrown around (Mood and Grudges in Enhancements). On unless the owner turns it off.
        internal bool GrudgesOn()
        {
            return ExperimentalFeatureOn("grudges");
        }

        // How long she sulks if she's left alone, in seconds. Five minutes unless the owner chose otherwise.
        internal double GrudgeSeconds()
        {
            string length = ExperimentalOption("grudges", "length", "5 minutes");
            if (length == "1 minute") return 60;
            if (length == "15 minutes") return 900;
            return 300;
        }
    }
}
