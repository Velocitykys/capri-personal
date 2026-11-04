using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content;

namespace Capri.Mobile;

internal sealed class MobileBrain
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string folder;
    private readonly string settingsFile;
    private readonly string historyFile;
    private readonly string memoryFile;
    private readonly string remindersFile;

    internal MobileSettings Settings { get; private set; }
    internal List<MobileChatLine> History { get; private set; }
    internal List<MobileMemory> Memories { get; private set; }
    internal List<MobileReminder> Reminders { get; private set; }

    internal MobileBrain(Context context)
    {
        folder = context.FilesDir.AbsolutePath;
        Directory.CreateDirectory(folder);
        settingsFile = Path.Combine(folder, "capri-settings.json");
        historyFile = Path.Combine(folder, "capri-conversation.json");
        memoryFile = Path.Combine(folder, "capri-memory.json");
        remindersFile = Path.Combine(folder, "capri-reminders.json");
        Settings = Read<MobileSettings>(settingsFile) ?? new MobileSettings();
        History = Read<List<MobileChatLine>>(historyFile) ?? new();
        Memories = Read<List<MobileMemory>>(memoryFile) ?? new();
        Reminders = Read<List<MobileReminder>>(remindersFile) ?? new();
    }

    private static T Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null; }
        catch { return null; }
    }

    private static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    internal void SaveSettings() => Write(settingsFile, Settings);
    internal void SaveHistory() => Write(historyFile, History);
    internal void SaveMemories() => Write(memoryFile, Memories);
    internal void SaveReminders() => Write(remindersFile, Reminders);

    internal void AddMemory(string text, string source = "manual")
    {
        text = (text ?? "").Trim();
        if (text.Length < 3) return;
        if (Memories.Any(m => string.Equals(m.Text, text, StringComparison.OrdinalIgnoreCase))) return;
        Memories.Add(new MobileMemory { Text = text, Source = source });
        if (Memories.Count > 300) Memories.RemoveAt(0);
        SaveMemories();
    }

    internal void RemoveMemory(string id)
    {
        Memories.RemoveAll(m => m.Id == id);
        SaveMemories();
    }

    internal void AddReminder(MobileReminder reminder)
    {
        Reminders.Add(reminder);
        SaveReminders();
    }

    internal void RemoveReminder(int id)
    {
        Reminders.RemoveAll(r => r.Id == id);
        SaveReminders();
    }

    internal void ClearHistory()
    {
        History.Clear();
        SaveHistory();
    }

    private void LearnFrom(string prompt)
    {
        string value = prompt.Trim();
        string lower = value.ToLowerInvariant();
        string[] explicitStarts = { "remember that ", "remember this: ", "save this: ", "don't forget that ", "dont forget that " };
        foreach (string start in explicitStarts)
        {
            if (lower.StartsWith(start, StringComparison.Ordinal))
            {
                AddMemory(value.Substring(start.Length).Trim(), "explicit");
                return;
            }
        }

        string[] factStarts = { "my name is ", "i prefer ", "i like ", "i love ", "i hate ", "i'm working on ", "i am working on ", "my goal is " };
        foreach (string start in factStarts)
        {
            if (lower.StartsWith(start, StringComparison.Ordinal) && value.Length <= 240)
            {
                AddMemory(value, "conversation");
                return;
            }
        }
    }

    private string RelevantMemory(string prompt)
    {
        var words = prompt.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).Distinct().ToHashSet();
        var ranked = Memories
            .Select(m => new { Memory = m, Score = words.Count(w => m.Text.ToLowerInvariant().Contains(w)) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Memory.CreatedAt)
            .Take(12)
            .Select(x => x.Memory.Text)
            .ToList();
        if (ranked.Count == 0) ranked = Memories.TakeLast(8).Select(m => m.Text).ToList();
        return ranked.Count == 0 ? "" : "\n\nThings you remember about this person (use naturally, never recite as a profile):\n- " + string.Join("\n- ", ranked);
    }

    internal async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
            throw new InvalidOperationException("Add your NVIDIA API key in Capri's settings first.");

        History.Add(new MobileChatLine { Role = "user", Content = prompt.Trim() });
        LearnFrom(prompt);
        if (History.Count > 80) History.RemoveRange(0, History.Count - 80);
        SaveHistory();

        var messages = new List<object>
        {
            new { role = "system", content = MobilePersonality.SystemPrompt + RelevantMemory(prompt) }
        };
        foreach (MobileChatLine line in History)
            messages.Add(new { role = line.Role, content = line.Content });

        string endpoint = (Settings.Endpoint ?? "").Trim().TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^"/chat/completions".Length];
        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint += "/chat/completions";
        else
            endpoint += "/v1/chat/completions";

        var payload = JsonSerializer.Serialize(new
        {
            model = Settings.Model,
            messages,
            temperature = 0.85,
            max_tokens = 700,
            stream = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey.Trim());
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("NVIDIA returned " + (int)response.StatusCode + ": " + Short(body));

        using JsonDocument document = JsonDocument.Parse(body);
        string answer = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        answer = answer.Trim();
        if (answer.Length == 0) throw new InvalidOperationException("Capri's AI returned an empty reply. Try again.");
        History.Add(new MobileChatLine { Role = "assistant", Content = answer });
        SaveHistory();
        return answer;
    }

    private static string Short(string text)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 400 ? text[..400] : text;
    }
}
