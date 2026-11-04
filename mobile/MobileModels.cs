using System;
using System.Collections.Generic;

namespace Capri.Mobile;

internal sealed class MobileChatLine
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

internal sealed class MobileSettings
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://integrate.api.nvidia.com/v1";
    public string Model { get; set; } = "nvidia/nemotron-3.5-super-vl-120b-a12b";
    public bool SpeakReplies { get; set; } = true;
}

internal sealed class MobileReminder
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime DueLocal { get; set; }
    public bool Completed { get; set; }
}

internal sealed class MobileMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Source { get; set; } = "manual";
}
