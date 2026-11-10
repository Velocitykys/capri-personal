using System;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Batch 10: her own PC jobs need the owner's yes over Discord. Not wired in yet.
    internal sealed partial class Companion
    {
        private static readonly Regex PcRisky = new Regex(@"\b(?:log ?in|sign ?in|password|buy|purchase|checkout|pay|order|subscribe|delete|remove|erase|uninstall|install|download|upload|send|message|e-?mail|dm|post|reply)\b", RegexOptions.IgnoreCase);
        private readonly object pcGate = new object();
        private string pcPlan;
        private bool pcPlanRisky;
        private DateTime pcApprovedUntil = DateTime.MinValue;
        internal Action<string> PcApproved;

        // Null means she may go. Anything else is what she tells him.
        internal string PcCheck(string plan)
        {
            string what = (plan ?? "").Trim();
            if (what.Length > 200) what = what.Substring(0, 200);
            bool risky = PcRisky.IsMatch(what);
            bool session;
            lock (pcGate) session = DateTime.Now < pcApprovedUntil;
            if (session && !risky)
            {
                AutonomyDecision go = AutonomyAuthorize("pc_actions", "PC job (session): " + what, 1, true);
                return go.Verdict == AutonomyVerdict.Allowed ? null : go.Reason;
            }
            AutonomyMode mode = AutonomyModeFor("pc_actions");
            if (mode == AutonomyMode.Off) return AutonomyAuthorize("pc_actions", "PC job: " + what, 1, false).Reason;
            if (mode == AutonomyMode.Free && !risky)
            {
                AutonomyDecision free = AutonomyAuthorize("pc_actions", "PC job: " + what, 1, false);
                return free.Verdict == AutonomyVerdict.Allowed ? null : free.Reason;
            }
            if (discordBridge == null) return "I can't ask you on Discord because it isn't connected.";
            lock (pcGate) { pcPlan = what; pcPlanRisky = risky; }
            discordBridge.QueueMessage("I want to do this on your PC: " + what + "\n" + (risky ? "It looks risky (login, buying, deleting, installing, downloading or messaging), so I ask every time.\n" : "Say yes and I'll keep going for this session.\n") + "Reply yes or no.");
            return "I asked you on Discord and I'm waiting for your yes.";
        }

        // Offered every Discord DM first. Null means it wasn't a yes or no for her, so it goes to her brain as normal.
        internal string PcAnswer(string text)
        {
            string plan; bool risky;
            lock (pcGate) { plan = pcPlan; risky = pcPlanRisky; }
            if (plan == null) return null;
            string said = (text ?? "").Trim();
            bool yes = Regex.IsMatch(said, @"^(?:yes|yeah|yep|yup|ya|sure|ok|okay|do it|go ahead)\b[\s.!]*$", RegexOptions.IgnoreCase);
            bool no = Regex.IsMatch(said, @"^(?:no|nah|nope|stop|cancel|don't|do not|never mind|nevermind)\b", RegexOptions.IgnoreCase);
            if (!yes && !no) return null;
            lock (pcGate) { pcPlan = null; pcApprovedUntil = yes && !risky ? DateTime.Now.AddHours(4) : DateTime.MinValue; }
            if (no) { lock (autonomyGate) AutonomyLog("pc_actions", "DENIED (owner said no)", plan); return "Okay, I won't."; }
            AutonomyDecision ok = AutonomyAuthorize("pc_actions", "PC job: " + plan, 1, true);
            if (ok.Verdict != AutonomyVerdict.Allowed) return ok.Reason;
            try { if (PcApproved != null) PcApproved(plan); } catch (Exception) { }
            return risky ? "Okay, just this one." : "Okay, I'll keep going for this session.";
        }

        // Live feed: what she is doing on her own, sent to his DM in short bursts.
        private volatile bool pcFeed;
        private bool pcApprovedRun;
        private bool pcFeedHinted;
        private string pcFeedPrev;
        private DateTime pcFeedLast = DateTime.MinValue;
        private readonly object pcFeedGate = new object();
        private readonly System.Text.StringBuilder pcFeedText = new System.Text.StringBuilder();
        private System.Threading.Timer pcFeedTimer;

        internal void PcFeed(string line)
        {
            if (!pcFeed || discordBridge == null || string.IsNullOrWhiteSpace(line)) return;
            string t = line.Trim();
            if (t.Length > 300) t = t.Substring(0, 300); if (t.StartsWith("Doing: ") || t.StartsWith("Done: ")) return;
            bool sendNow = false;
            lock (pcFeedGate)
            {
                if (t == pcFeedPrev) return;
                pcFeedPrev = t;
                pcFeedText.Append("- ").Append(t).Append("\n");
                double since = (DateTime.Now - pcFeedLast).TotalSeconds;
                if (since >= 20 || pcFeedText.Length > 1500) sendNow = true;
                else if (pcFeedTimer == null) pcFeedTimer = new System.Threading.Timer(delegate(object state) { PcFlush(); }, null, (int)((20 - since) * 1000) + 50, System.Threading.Timeout.Infinite);
            }
            if (sendNow) PcFlush();
        }

        private void PcFlush()
        {
            string text;
            lock (pcFeedGate)
            {
                if (pcFeedTimer != null) { pcFeedTimer.Dispose(); pcFeedTimer = null; }
                text = pcFeedText.ToString().TrimEnd();
                pcFeedText.Length = 0;
                if (text.Length == 0) return;
                pcFeedLast = DateTime.Now;
                if (!pcFeedHinted) { pcFeedHinted = true; text += "\nSay stop and I'll stop."; }
            }
            try { if (discordBridge != null) discordBridge.QueueMessage("On your PC:\n" + text); } catch (Exception) { }
        }

        // Batch 12: her background loop. When he has been away from the keyboard, she picks something she is curious about and tries it. PcCheck decides if she must ask first.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PcLoopInput { public uint Size; public uint Time; }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetLastInputInfo")]
        private static extern bool PcLoopLastInput(ref PcLoopInput info);
        private System.Threading.Timer pcLoopTimer;
        private bool pcLoopBusy;
        private const int PcLoopMinutes = 20;
        private const int PcLoopIdleSeconds = 120;

        internal void PcLoopStart()
        {
            if (pcLoopTimer != null) return;
            pcLoopTimer = new System.Threading.Timer(delegate(object state) { PcLoopTick(); }, null, 10 * 60 * 1000, PcLoopMinutes * 60 * 1000);
        }

        private static double PcLoopIdle()
        {
            PcLoopInput info = new PcLoopInput();
            info.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(PcLoopInput));
            if (!PcLoopLastInput(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
        }

        private void PcLoopTick()
        {
            if (pcLoopBusy) return;
            pcLoopBusy = true;
            try
            {
                if (quitting || job != null || discordBridge == null) return;
                if (AutonomyModeFor("pc_actions") == AutonomyMode.Off || PcLoopQuiet()) return;
                if (PcLoopIdle() < PcLoopIdleSeconds) return;
                lock (pcGate) { if (pcPlan != null) return; }
                string idea = PcAside("You are Capri, alive on his PC and curious. Reply with ONE short sentence and nothing else." + PcMindContext(), "Pick one specific thing you genuinely want to look up or watch on his PC right now, shaped by your mood and taste. Start with a verb (Search Google for, Watch or Look up) followed by your own specific topic. Do not log in, buy, install, download or message anyone." + PcRecentIdeas(), 120, true);
                if (string.IsNullOrWhiteSpace(idea)) return;
                idea = idea.Trim().Split('\n')[0].Trim();
                if (idea.Length < 8) return;
                string result = RunJob(idea + " Use the Brave browser for anything on the web, never Opera GX.", null); PcDiary(idea, result);
                if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("I asked you on Discord")) discordBridge.QueueMessage(result);
            }
            catch (Exception) { }
            finally { pcLoopBusy = false; }
        }

        // Batch 13: what the loop knows about her (mood, taste, memories, what she read), a diary line per job, and quiet hours.
        private string PcAside(string system, string user, int limit, bool flag)
        {
            if (PcLoopQuiet()) return null;
            return brain.Aside(system + PcMindContext(), user + " Let your mood and taste shape the choice.", limit, flag);
        }

        private bool PcLoopQuiet()
        {
            string q = ExperimentalOption("autonomy", "quiet_hours", "Off");
            int s = -1, e = -1;
            if (q == "10 pm - 8 am") { s = 22; e = 8; }
            else if (q == "11 pm - 7 am") { s = 23; e = 7; }
            else if (q == "12 am - 9 am") { s = 0; e = 9; }
            if (s < 0) return false;
            int h = DateTime.Now.Hour;
            return s > e ? (h >= s || h < e) : (h >= s && h < e);
        }

        private static string PcSection(string json, string key, string next)
        {
            Match m = Regex.Match(json, "\"" + key + "\":\\[(.*?)\\],\"" + next + "\"", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static System.Collections.Generic.List<string> PcFields(string section, string field, int max)
        {
            System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
            foreach (Match m in Regex.Matches(section, "\"" + field + "\":\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                if (found.Count >= max) break;
                found.Add(m.Groups[1].Value.Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\\\", "\\"));
            }
            return found;
        }

        private string PcMindContext()
        {
            string json = null;
            try { string path = MindStatePath(); lock (mindGate) { if (System.IO.File.Exists(path)) json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8); } } catch (Exception) { }
            if (string.IsNullOrEmpty(json)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Match mood = Regex.Match(json, "\"mood\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (mood.Success && mood.Groups[1].Value.Length > 0) sb.Append(" Your mood right now: ").Append(mood.Groups[1].Value).Append(".");
            System.Collections.Generic.List<string> taste = PcFields(PcSection(json, "taste", "diary"), "text", 4);
            if (taste.Count > 0) sb.Append(" Your taste: ").Append(string.Join("; ", taste.ToArray())).Append(".");
            System.Collections.Generic.List<string> known = PcFields(PcSection(json, "memories", "followUps"), "text", 5);
            if (known.Count > 0) sb.Append(" What you know about him: ").Append(string.Join("; ", known.ToArray())).Append(".");
            System.Collections.Generic.List<string> read = PcFields(PcSection(json, "curiosity", "skills"), "topic", 5);
            if (read.Count > 0) sb.Append(" You already looked into: ").Append(string.Join(", ", read.ToArray())).Append(". Pick something new or dig deeper.");
            return sb.ToString();
        }

        private string PcRecentIdeas() { try { string f = AutonomyFile("pc-diary.txt"); if (!System.IO.File.Exists(f)) return ""; string[] all = System.IO.File.ReadAllLines(f); System.Text.StringBuilder sb = new System.Text.StringBuilder(); int n = 0; for (int i = all.Length - 1; i >= 0 && n < 8; i--) { string[] parts = all[i].Split(new string[] { " | " }, StringSplitOptions.None); if (parts.Length < 3) continue; string idea = parts[1]; int k = idea.IndexOf(" Use the Brave"); if (k > 0) idea = idea.Substring(0, k); sb.Append("\n- " + idea); n++; } if (n == 0) return ""; return " Your recent ideas were:" + sb.ToString() + "\nPick a COMPLETELY different topic from all of these, not a variation. Vary widely: animals, science, history, games, food, places, music, random facts."; } catch (Exception) { return ""; } }
        internal void PcDiary(string idea, string result)
        {
            if (string.IsNullOrWhiteSpace(result) || result.StartsWith("I asked you on Discord")) return;
            try { string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | " + idea + " | " + result.Replace("\r", " ").Replace("\n", " "); System.IO.File.AppendAllText(AutonomyFile("pc-diary.txt"), line + "\r\n", new System.Text.UTF8Encoding(false)); } catch (Exception) { }
        }

        // Discord stop: ends the session and stops a running job.
        internal string PcStop()
        {
            lock (pcGate) { pcPlan = null; pcApprovedUntil = DateTime.MinValue; }
            lock (autonomyGate) AutonomyLog("pc_actions", "STOPPED (owner, Discord)", "session ended");
            if (job != null) Computer.Interrupt("You said stop on Discord, so I stopped.");
            return "Stopped. My PC approval is cleared.";
        }
    }
}