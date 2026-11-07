using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class DiscordBridgeRequest
    {
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "secret")] public string Secret;
        [DataMember(Name = "message64")] public string Message64;
        [DataMember(Name = "sharedMemory")] public string SharedMemory;
        [DataMember(Name = "publicContext")] public string PublicContext;
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "minutes")] public int? Minutes;
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "when")] public string When;
        [DataMember(Name = "nextAt")] public string NextAt;
        [DataMember(Name = "discordSaid")] public bool DiscordSaid;
    }

    [DataContract]
    internal sealed class DiscordOutboxMessage
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "text")] public string Text;
    }

    [DataContract]
    internal sealed class DiscordReminderMessage
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "text")] public string Text;
    }

    [DataContract]
    internal sealed class DiscordBridgeResponse
    {
        [DataMember(Name = "ok")] public bool Ok;
        [DataMember(Name = "value", EmitDefaultValue = false)] public string Value;
        [DataMember(Name = "handled", EmitDefaultValue = false)] public bool? Handled;
    }

    // Local-only bridge for the owner's Discord bot. The random secret is stored beside Capri's
    // private data; the bot reads it from the same per-user folder.
    internal sealed class DiscordBridge : IDisposable
    {
        private readonly string secret;
        private readonly Reminders reminders;
        private readonly Func<string, string, bool, string> ask;
        private readonly Func<string, string, string> publicAsk;
        private readonly Func<string> status;
        private readonly string outboxPath;
        private readonly System.Collections.Generic.List<DiscordOutboxMessage> outbox = new System.Collections.Generic.List<DiscordOutboxMessage>();
        private readonly Thread listener;
        private readonly object gate = new object();
        private readonly object askGate = new object();
        private readonly object publicAskGate = new object();
        private NamedPipeServerStream waiting;
        private volatile bool stopping;

        internal DiscordBridge(string data, Reminders reminders, Func<string, string, bool, string> ask, Func<string, string, string> publicAsk, Func<string> status)
        {
            this.reminders = reminders;
            this.ask = ask;
            this.publicAsk = publicAsk;
            this.status = status;
            Directory.CreateDirectory(data);
            outboxPath = Path.Combine(data, "discord-outbox.json");
            try
            {
                if (File.Exists(outboxPath))
                    using (FileStream input = File.OpenRead(outboxPath))
                        outbox.AddRange((System.Collections.Generic.List<DiscordOutboxMessage>)new DataContractJsonSerializer(typeof(System.Collections.Generic.List<DiscordOutboxMessage>)).ReadObject(input));
            }
            catch (Exception error) { Brain.Log("Discord bridge", -1, "Couldn't read the private Discord outbox: " + error.Message); }
            secret = Secret(Path.Combine(data, "discord-bridge.secret"));
            listener = new Thread(Listen);
            listener.IsBackground = true;
            listener.Name = "Capri Discord bridge";
            listener.Start();
        }

        private static string Secret(string path)
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (existing.Length >= 32) return existing;
            }

            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            string made = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            try
            {
                if (File.Exists(path)) File.WriteAllText(path, made, new UTF8Encoding(false));
                else
                {
                    using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (StreamWriter writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(made);
                }
                return made;
            }
            catch (IOException)
            {
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (existing.Length >= 32) return existing;
                }
                throw;
            }
        }

        internal string QueueMessage(string text)
        {
            string clean = (text ?? "").Trim();
            if (clean.Length == 0) return "What should I send to your private Discord DM?";
            if (clean.Length > 1800) return "That message is too long for one Discord DM. Keep it under 1800 characters.";
            lock (gate)
            {
                outbox.Add(new DiscordOutboxMessage { Id = Guid.NewGuid().ToString("N"), Text = clean });
                SaveOutbox();
            }
            return "Queued a message for your private Discord DM.";
        }

        private void SaveOutbox()
        {
            string temporary = outboxPath + ".new";
            using (FileStream output = File.Create(temporary))
                new DataContractJsonSerializer(typeof(System.Collections.Generic.List<DiscordOutboxMessage>)).WriteObject(output, outbox);
            if (File.Exists(outboxPath)) File.Replace(temporary, outboxPath, null); else File.Move(temporary, outboxPath);
        }

        private System.Collections.Generic.List<DiscordOutboxMessage> Outbox()
        {
            lock (gate)
            {
                System.Collections.Generic.List<DiscordOutboxMessage> copy = new System.Collections.Generic.List<DiscordOutboxMessage>();
                foreach (DiscordOutboxMessage item in outbox)
                    if (item != null) copy.Add(new DiscordOutboxMessage { Id = item.Id, Text = item.Text });
                return copy;
            }
        }

        private void AcknowledgeOutbox(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (gate)
            {
                int index = outbox.FindIndex(item => item != null && item.Id == id);
                if (index < 0) return;
                outbox.RemoveAt(index);
                SaveOutbox();
            }
        }

        private void Listen()
        {
            while (!stopping)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream("CapriDiscordBridge", PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.None);
                    lock (gate) waiting = pipe;
                    pipe.WaitForConnection();
                    lock (gate) if (waiting == pipe) waiting = null;
                    if (stopping) { pipe.Dispose(); break; }
                    ThreadPool.QueueUserWorkItem(Serve, pipe);
                }
                catch (ObjectDisposedException)
                {
                    if (pipe != null) pipe.Dispose();
                    if (!stopping) Thread.Sleep(250);
                }
                catch (IOException error)
                {
                    if (pipe != null) pipe.Dispose();
                    if (!stopping) { Brain.Log("Discord bridge", -1, error.Message); Thread.Sleep(1000); }
                }
                catch (Exception error)
                {
                    if (pipe != null) pipe.Dispose();
                    if (!stopping) { Brain.Log("Discord bridge", -1, error.Message); Thread.Sleep(1000); }
                }
            }
        }

        private void Serve(object state)
        {
            using (NamedPipeServerStream pipe = (NamedPipeServerStream)state)
            {
                try
                {
                    using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                    using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                    {
                        string line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line) || line.Length > 1048576) throw new IOException("Invalid Discord bridge request.");
                        DiscordBridgeRequest request;
                        using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                            request = (DiscordBridgeRequest)new DataContractJsonSerializer(typeof(DiscordBridgeRequest)).ReadObject(input);

                        DiscordBridgeResponse response;
                        if (request == null || !SameSecret(secret, request.Secret))
                            response = new DiscordBridgeResponse { Ok = false, Value = "Discord bridge authentication failed." };
                        else
                            response = Handle(request);

                        writer.Write(Serialize(response));
                        writer.Write("\n");
                        writer.Flush();
                    }
                }
                catch (Exception error)
                {
                    Brain.Log("Discord bridge", -1, error.Message);
                    try
                    {
                        byte[] body = Encoding.UTF8.GetBytes("{\"ok\":false,\"value\":\"Capri bridge error.\"}\n");
                        pipe.Write(body, 0, body.Length);
                        pipe.Flush();
                    }
                    catch { }
                }
            }
        }

        private DiscordBridgeResponse Handle(DiscordBridgeRequest request)
        {
            string text = Decode(request.Message64);

            switch (request.Type ?? "")
            {
                case "personality":
                    return new DiscordBridgeResponse { Ok = true, Value = Brain.PublicCharacter };

                case "ask":
                case "spontaneous":
                    if (ask == null) throw new InvalidOperationException("Capri's brain isn't ready.");
                    if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("There was no message to send to Capri.");
                    lock (askGate) return new DiscordBridgeResponse { Ok = true, Value = ask(text, request.Type == "ask" ? request.SharedMemory : null, request.Type == "spontaneous") };

                case "publicAsk":
                    if (publicAsk == null) throw new InvalidOperationException("Capri's public brain isn't ready.");
                    if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("There was no public message to send to Capri.");
                    lock (publicAskGate) return new DiscordBridgeResponse { Ok = true, Value = publicAsk(text, request.PublicContext) };

                case "status":
                    return new DiscordBridgeResponse { Ok = true, Value = status == null ? "0|0|" : status() };

                case "reminder":
                    string answer = reminders == null ? null : reminders.DiscordHandle(text);
                    return new DiscordBridgeResponse { Ok = true, Handled = answer != null, Value = answer ?? "" };

                case "reminders_due":
                    return new DiscordBridgeResponse { Ok = true, Value = Serialize(DueReminders()) };

                case "reminder_ack":
                    return new DiscordBridgeResponse { Ok = true, Value = reminders == null ? "Couldn't access reminders." : reminders.DiscordAcknowledge() };

                case "reminder_snooze":
                    int minutes = request.Minutes.HasValue ? Math.Max(1, Math.Min(1440, request.Minutes.Value)) : 10;
                    return new DiscordBridgeResponse { Ok = true, Value = reminders == null ? "Couldn't access reminders." : reminders.DiscordSnooze(minutes) };

                case "reminder_delivery_failed":
                    if (reminders != null) reminders.DiscordDeliveryFailed(request.Id);
                    return new DiscordBridgeResponse { Ok = true, Value = "Retry queued." };

                case "reminder_import":
                    return new DiscordBridgeResponse { Ok = true, Value = reminders == null ? "Couldn't access reminders." : reminders.ImportDiscordReminder(request.Id, text, request.When, request.DiscordSaid, request.NextAt) };

                case "outbox_take":
                    return new DiscordBridgeResponse { Ok = true, Value = Serialize(Outbox()) };

                case "outbox_ack":
                    AcknowledgeOutbox(request.Id);
                    return new DiscordBridgeResponse { Ok = true, Value = "Delivered." };

                default:
                    return new DiscordBridgeResponse { Ok = false, Value = "Unknown Capri bridge request." };
            }
        }

        private System.Collections.Generic.List<DiscordReminderMessage> DueReminders()
        {
            System.Collections.Generic.List<Reminder> due = reminders == null
                ? new System.Collections.Generic.List<Reminder>()
                : reminders.TakeForDiscord(DateTime.Now);
            System.Collections.Generic.List<DiscordReminderMessage> messages = new System.Collections.Generic.List<DiscordReminderMessage>();
            foreach (Reminder item in due)
                if (item != null) messages.Add(new DiscordReminderMessage { Id = item.Id, Text = item.Text });
            return messages;
        }

        private static string Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch (FormatException) { throw new IOException("The Discord message wasn't valid UTF-8 data."); }
        }

        private static string Serialize<T>(T value)
        {
            using (MemoryStream output = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(output, value);
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static bool SameSecret(string left, string right)
        {
            if (left == null || right == null) return false;
            int difference = left.Length ^ right.Length;
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                char a = i < left.Length ? left[i] : (char)0;
                char b = i < right.Length ? right[i] : (char)0;
                difference |= a ^ b;
            }
            return difference == 0;
        }

        public void Dispose()
        {
            stopping = true;
            lock (gate)
            {
                if (waiting != null)
                {
                    try { waiting.Dispose(); }
                    catch (IOException) { }
                    waiting = null;
                }
            }
            if (listener != null && listener.IsAlive) listener.Join(1000);
        }
    }
}
