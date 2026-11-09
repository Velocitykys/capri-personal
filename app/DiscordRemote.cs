using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Remote control from the owner's private Discord DM (batch 2). Every request goes through the Autonomy rules first
    // (PC actions area, daily budget, log), so with PC actions Off none of this does anything. The bot only asks the owner
    // for a "yes"; this side decides what is allowed. Answers are "verdict|text|used/limit" where verdict is
    // done, confirm, denied or failed, so the bot can fail closed.
    internal sealed partial class Companion
    {
        // A saved command whose steps mention any of these always needs the owner's yes, even with PC actions on Free.
        private static readonly Regex SensitiveSteps = new Regex(
            @"\b(?:delete|remove|erase|wipe|format|shut ?down|power ?off|restart|reboot|uninstall|kill|terminate|close all|empty (?:the )?recycle|sign ?out|log ?off|lock|sleep|hibernate)\b",
            RegexOptions.IgnoreCase);

        private static string PcClean(string text)
        {
            string clean = (text ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length > 600 ? clean.Substring(0, 600) : clean;
        }

        private static string PcVerdict(AutonomyDecision decision)
        {
            return decision.Verdict.ToString().ToLowerInvariant() + "|" + PcClean(decision.Reason) + "|" + decision.Used + "/" + decision.Limit;
        }

        internal string DiscordPcAction(string action, string target, bool confirmed)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            target = (target ?? "").Trim();
            try
            {
                if (action == "list_commands")
                {
                    List<Routine> items = EnhancementRoutines();
                    List<string> names = new List<string>();
                    foreach (Routine item in items)
                        if (item != null && !string.IsNullOrWhiteSpace(item.Phrase)) names.Add(PcClean(item.Phrase));
                    if (names.Count == 0) return "done|You have no saved commands yet. Add some under Enhancements > Custom Commands.|0/0";
                    return "done|Your saved commands: " + string.Join("; ", names.ToArray()) + "|0/0";
                }

                if (action == "open_app")
                {
                    if (target.Length == 0) return "failed|Which app should I open?|0/0";
                    AutonomyDecision decision = AutonomyAuthorize("pc_actions", "Discord: open app " + target, 1, confirmed);
                    if (decision.Verdict != AutonomyVerdict.Allowed) return PcVerdict(decision);
                    string result = Hands.OpenAppFromDiscord(target);
                    bool missing = result.StartsWith("No app called", StringComparison.Ordinal) || result.StartsWith("Which app", StringComparison.Ordinal);
                    return (missing ? "failed" : "done") + "|" + PcClean(result) + "|" + decision.Used + "/" + decision.Limit;
                }

                if (action == "run_command")
                {
                    if (target.Length == 0) return "failed|Which saved command should I run?|0/0";
                    Routine found = null;
                    foreach (Routine item in EnhancementRoutines())
                        if (item != null && string.Equals((item.Phrase ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase)) { found = item; break; }
                    if (found == null) return "failed|I don't have a saved command called \"" + PcClean(target) + "\". Say \"commands\" to see your list.|0/0";

                    bool sensitive = SensitiveSteps.IsMatch(found.Steps ?? "");
                    if (sensitive && !confirmed && AutonomyModeFor("pc_actions") != AutonomyMode.Off)
                        return "confirm|\"" + PcClean(found.Phrase) + "\" looks sensitive (" + PcClean(found.Steps) + ") so I need your yes first.|0/0";

                    AutonomyDecision decision = AutonomyAuthorize("pc_actions", "Discord: run saved command " + found.Phrase, 1, confirmed);
                    if (decision.Verdict != AutonomyVerdict.Allowed) return PcVerdict(decision);
                    if (!IsHandleCreated) return "failed|Capri's window isn't ready yet, try again in a moment.|" + decision.Used + "/" + decision.Limit;

                    string phrase = found.Phrase;
                    BeginInvoke(new Action(delegate
                    {
                        string outcome;
                        try { outcome = EnhancementRunRoutine(phrase); }
                        catch (Exception error) { outcome = "That command failed: " + Voice.Short(error.Message); }
                        try { if (discordBridge != null) discordBridge.QueueMessage("Finished \"" + phrase + "\": " + PcClean(outcome)); }
                        catch (Exception) { }
                    }));
                    return "done|Started \"" + PcClean(phrase) + "\". I'll DM you how it went.|" + decision.Used + "/" + decision.Limit;
                }

                return "failed|I don't know how to do that remotely yet.|0/0";
            }
            catch (Exception error)
            {
                return "failed|That didn't work: " + PcClean(error.Message) + "|0/0";
            }
        }
    }
}
