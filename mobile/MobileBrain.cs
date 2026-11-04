using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Capri.Mobile;

internal sealed class MobileChatLine
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}

internal sealed class MobileSettings
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://integrate.api.nvidia.com/v1";
    public string Model { get; set; } = "nvidia/nemotron-3.5-super-vl-120b-a12b";
}

internal sealed class MobileBrain
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string folder;
    private readonly string settingsFile;
    private readonly string historyFile;

    internal MobileSettings Settings { get; private set; }
    internal List<MobileChatLine> History { get; private set; } = new List<MobileChatLine>();

    internal const string Personality = MobilePersonality.SystemPrompt;

    internal MobileBrain()
    {
        folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapriMobile");
        Directory.CreateDirectory(folder);
        settingsFile = Path.Combine(folder, "settings.json");
        historyFile = Path.Combine(folder, "conversation.json");
        Settings = Read<MobileSettings>(settingsFile) ?? new MobileSettings();
        History = Read<List<MobileChatLine>>(historyFile) ?? new List<MobileChatLine>();
    }

    private static T Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null; }
        catch { return null; }
    }

    internal void SaveSettings()
    {
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    private void SaveHistory()
    {
        File.WriteAllText(historyFile, JsonSerializer.Serialize(History, JsonOptions));
    }

    internal void ClearHistory()
    {
        History.Clear();
        SaveHistory();
    }

    internal async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
            throw new InvalidOperationException("Add your NVIDIA API key in Capri's settings first.");

        History.Add(new MobileChatLine { Role = "user", Content = prompt.Trim() });
        if (History.Count > 40) History.RemoveRange(0, History.Count - 40);
        var messages = new List<object>
        {
            new { role = "system", content = Personality }
        };
        foreach (MobileChatLine line in History)
            messages.Add(new { role = line.Role, content = line.Content });

        var payload = JsonSerializer.Serialize(new
        {
            model = Settings.Model,
            messages,
            temperature = 0.85,
            max_tokens = 512,
            stream = false
        });
        string endpoint = (Settings.Endpoint ?? "").Trim().TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint.Substring(0, endpoint.Length - "/chat/completions".Length);
        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint += "/chat/completions";
        else
            endpoint += "/v1/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey.Trim());
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("NVIDIA returned " + (int)response.StatusCode + ": " + Short(body));

        using JsonDocument document = JsonDocument.Parse(body);
        string answer = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        answer = (answer ?? "").Trim();
        if (answer.Length == 0)
            throw new InvalidOperationException("Capri's AI returned an empty reply. Try again.");
        History.Add(new MobileChatLine { Role = "assistant", Content = answer });
        SaveHistory();
        return answer;
    }

    private static string Short(string text)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 500 ? text.Substring(0, 500) : text;
    }
}
