using System;
using System.Linq;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Speech;
using Android.Speech.Tts;
using Android.Views;
using Android.Widget;
using AndroidX = Android;

namespace Capri.Mobile;

[Activity(
    Label = "Capri",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
public sealed class MainActivity : Activity, TextToSpeech.IOnInitListener
{
    private const int SpeechRequestCode = 4201;
    private const int AudioPermissionCode = 4202;
    private MobileBrain brain;
    private TextToSpeech speech;
    private LinearLayout root;
    private LinearLayout messageList;
    private ScrollView chatScroll;
    private EditText composer;
    private Button sendButton;
    private TextView statusText;
    private string lastCapriReply = "";
    private bool speechReady;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetStatusBarColor(Color.ParseColor("#0B1020"));
        Window?.SetNavigationBarColor(Color.ParseColor("#0B1020"));
        brain = new MobileBrain(this);
        speech = new TextToSpeech(this, this);
        BuildChat();
        HandleSharedText(Intent);
    }

    public void OnInit(OperationResult status)
    {
        speechReady = status == OperationResult.Success;
        if (speechReady) speech.SetLanguage(Java.Util.Locale.Default);
    }

    protected override void OnNewIntent(Intent intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleSharedText(intent);
    }

    private void HandleSharedText(Intent intent)
    {
        if (intent?.Action == Intent.ActionSend && intent.Type == "text/plain")
        {
            string shared = intent.GetStringExtra(Intent.ExtraText);
            if (!string.IsNullOrWhiteSpace(shared) && composer != null)
            {
                composer.Text = shared;
                composer.SetSelection(composer.Text.Length);
            }
        }
    }

    private static int Dp(float value) => (int)(value * Android.Content.Res.Resources.System.DisplayMetrics.Density + 0.5f);
    private TextView Label(string text, float size, string color = "#F2F5FF")
    {
        return new TextView(this)
        {
            Text = text,
            TextSize = size,
            Gravity = GravityFlags.CenterVertical
        }.Also(v => v.SetTextColor(Color.ParseColor(color)));
    }

    private Button ActionButton(string text, EventHandler click)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.Click += click;
        return button;
    }

    private void BuildChat()
    {
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.ParseColor("#0B1020"));
        root.SetPadding(Dp(14), Dp(10), Dp(14), Dp(10));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var avatar = new ImageView(this);
        avatar.SetImageResource(Resource.Drawable.capri);
        avatar.SetScaleType(ImageView.ScaleType.CenterCrop);
        avatar.SetBackgroundColor(Color.ParseColor("#17243D"));
        header.AddView(avatar, new LinearLayout.LayoutParams(Dp(76), Dp(76)));

        var identity = new LinearLayout(this) { Orientation = Orientation.Vertical };
        identity.SetPadding(Dp(12), 0, 0, 0);
        identity.AddView(Label("CAPRI", 22, "#8CB8FF"));
        identity.AddView(Label("Your own little Night City gremlin", 12, "#AAB4C8"));
        header.AddView(identity, new LinearLayout.LayoutParams(0, -2, 1));
        header.AddView(ActionButton("⚙", (_, _) => ShowSettings()), new LinearLayout.LayoutParams(Dp(52), Dp(48)));
        root.AddView(header);

        statusText = Label("LOCAL COMPANION  •  READY WHEN YOU ARE", 10, "#7890B7");
        statusText.SetPadding(0, Dp(8), 0, Dp(8));
        root.AddView(statusText);

        chatScroll = new ScrollView(this) { FillViewport = true };
        messageList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        messageList.SetPadding(0, Dp(8), 0, Dp(8));
        chatScroll.AddView(messageList);
        root.AddView(chatScroll, new LinearLayout.LayoutParams(-1, 0, 1));

        var composerRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        composerRow.SetGravity(GravityFlags.CenterVertical);
        composer = new EditText(this)
        {
            Hint = "Message Capri…",
            TextSize = 15,
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagCapSentences
        };
        composer.SetSingleLine(false);
        composer.SetMaxLines(4);
        composer.SetTextColor(Color.ParseColor("#F2F5FF"));
        composer.SetHintTextColor(Color.ParseColor("#8290A8"));
        composer.SetBackgroundColor(Color.ParseColor("#182238"));
        composer.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        composerRow.AddView(composer, new LinearLayout.LayoutParams(0, Dp(52), 1));
        composerRow.AddView(ActionButton("🎙", (_, _) => StartVoiceInput()), new LinearLayout.LayoutParams(Dp(48), Dp(52)));
        sendButton = ActionButton("Send", async (_, _) => await SendMessage());
        composerRow.AddView(sendButton, new LinearLayout.LayoutParams(Dp(70), Dp(52)));
        root.AddView(composerRow);

        var nav = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        nav.SetGravity(GravityFlags.Center);
        nav.AddView(ActionButton("Memory", (_, _) => ShowMemory()), new LinearLayout.LayoutParams(0, Dp(46), 1));
        nav.AddView(ActionButton("Reminders", (_, _) => ShowReminders()), new LinearLayout.LayoutParams(0, Dp(46), 1));
        nav.AddView(ActionButton("Phone", (_, _) => ShowPhoneTools()), new LinearLayout.LayoutParams(0, Dp(46), 1));
        nav.AddView(ActionButton("Share", (_, _) => ShareLastReply()), new LinearLayout.LayoutParams(0, Dp(46), 1));
        root.AddView(nav);

        SetContentView(root);
        if (brain.History.Count == 0)
            AddMessage("assistant", "Oh, there you are. Took you long enough. I'm still getting my bearings, choom.");
        else
            foreach (var line in brain.History) AddMessage(line.Role, line.Content);
    }

    private void AddMessage(string role, string text)
    {
        bool capri = role == "assistant";
        var bubble = new TextView(this)
        {
            Text = text,
            TextSize = 15,
            Gravity = GravityFlags.CenterVertical
        };
        bubble.SetTextColor(Color.ParseColor("#F0F3FA"));
        bubble.SetPadding(Dp(13), Dp(10), Dp(13), Dp(10));
        bubble.SetBackgroundColor(Color.ParseColor(capri ? "#1B2A43" : "#23466B"));
        var line = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        line.SetGravity(capri ? GravityFlags.Left : GravityFlags.Right);
        var lp = new LinearLayout.LayoutParams(-2, -2);
        lp.SetMargins(0, Dp(4), 0, Dp(4));
        bubble.SetMaxWidth(Dp(310));
        line.AddView(bubble, lp);
        messageList.AddView(line);
        if (capri) lastCapriReply = text;
        chatScroll?.Post(() => chatScroll.FullScroll(FocusSearchDirection.Down));
    }

    private async Task SendMessage()
    {
        string prompt = composer.Text?.Trim() ?? "";
        if (prompt.Length == 0) return;
        composer.Text = "";
        sendButton.Enabled = false;
        statusText.Text = "CAPRI IS THINKING…";
        AddMessage("user", prompt);
        try
        {
            string answer = await brain.AskAsync(prompt);
            AddMessage("assistant", answer);
            if (brain.Settings.SpeakReplies && speechReady)
                speech.Speak(answer, QueueMode.Flush, null, "capri-" + Guid.NewGuid().ToString("N"));
            statusText.Text = "LOCAL COMPANION  •  ONLINE";
        }
        catch (Exception error)
        {
            string localReply = brain.OfflineReply(prompt);
            AddMessage("assistant", localReply + " (" + error.Message + ")");
            statusText.Text = "OFFLINE MODE  •  LOCAL DATA AVAILABLE";
        }
        finally
        {
            sendButton.Enabled = true;
            composer.RequestFocus();
        }
    }

    private void StartVoiceInput()
    {
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
        {
            RequestPermissions(new[] { Manifest.Permission.RecordAudio }, AudioPermissionCode);
            return;
        }
        LaunchSpeechRecognizer();
    }

    private void LaunchSpeechRecognizer()
    {
        try
        {
            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPrompt, "Talk to Capri");
            intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            StartActivityForResult(intent, SpeechRequestCode);
        }
        catch (Exception error)
        {
            AddMessage("assistant", "Voice input isn't available on this phone right now: " + error.Message);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == AudioPermissionCode && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            LaunchSpeechRecognizer();
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == SpeechRequestCode && resultCode == Result.Ok && data != null)
        {
            var results = data.GetStringArrayListExtra(RecognizerIntent.ExtraResults);
            string spoken = results?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(spoken))
            {
                composer.Text = spoken;
                await SendMessage();
            }
        }
    }

    private void ShowSettings()
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(18), Dp(8), Dp(18), Dp(4));
        var key = new EditText(this) { Hint = "NVIDIA API key", Text = brain.Settings.ApiKey };
        key.SetSingleLine(true);
        key.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
        var endpoint = new EditText(this) { Hint = "API endpoint", Text = brain.Settings.Endpoint };
        endpoint.SetSingleLine(true);
        var model = new EditText(this) { Hint = "Model ID", Text = brain.Settings.Model };
        model.SetSingleLine(true);
        var speak = new CheckBox(this) { Text = "Speak Capri's replies aloud", Checked = brain.Settings.SpeakReplies };
        panel.AddView(key);
        panel.AddView(endpoint);
        panel.AddView(model);
        panel.AddView(speak);
        panel.AddView(Label("This connection and all app data are separate from desktop Capri. The API key stays in this app's private storage.", 12, "#AAB4C8"));
        new AlertDialog.Builder(this)
            .SetTitle("Capri connection")
            .SetView(panel)
            .SetNegativeButton("Cancel", (s, e) => { })
            .SetNeutralButton("Clear chat", (s, e) =>
            {
                brain.ClearHistory();
                messageList.RemoveAllViews();
                AddMessage("assistant", "Fresh page. Don't get used to the peace and quiet.");
            })
            .SetPositiveButton("Save", (s, e) =>
            {
                brain.Settings.ApiKey = key.Text?.Trim() ?? "";
                brain.Settings.Endpoint = string.IsNullOrWhiteSpace(endpoint.Text) ? "https://integrate.api.nvidia.com/v1" : endpoint.Text.Trim();
                brain.Settings.Model = string.IsNullOrWhiteSpace(model.Text) ? "nvidia/nemotron-3.5-super-vl-120b-a12b" : model.Text.Trim();
                brain.Settings.SpeakReplies = speak.Checked;
                brain.SaveSettings();
                AddMessage("assistant", "Connection settings saved. My brain's plugged in.");
            })
            .Show();
    }

    private void ShowMemory()
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(18), Dp(8), Dp(18), Dp(8));
        var input = new EditText(this) { Hint = "Something Capri should remember…" };
        panel.AddView(input);
        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        foreach (var memory in brain.Memories.OrderByDescending(m => m.CreatedAt))
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            var label = Label(memory.Text, 13);
            row.AddView(label, new LinearLayout.LayoutParams(0, -2, 1));
            var remove = ActionButton("×", (_, _) => { brain.RemoveMemory(memory.Id); ShowMemory(); });
            row.AddView(remove, new LinearLayout.LayoutParams(Dp(42), Dp(42)));
            list.AddView(row);
        }
        var scroll = new ScrollView(this);
        scroll.AddView(list);
        panel.AddView(scroll, new LinearLayout.LayoutParams(-1, Dp(300)));
        new AlertDialog.Builder(this)
            .SetTitle("Capri's memory (" + brain.Memories.Count + ")")
            .SetView(panel)
            .SetNegativeButton("Close", (s, e) => { })
            .SetPositiveButton("Remember", (s, e) =>
            {
                brain.AddMemory(input.Text, "manual");
                ShowMemory();
            })
            .Show();
    }

    private void ShowReminders()
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(18), Dp(8), Dp(18), Dp(8));
        foreach (var reminder in brain.Reminders.Where(r => !r.Completed).OrderBy(r => r.DueLocal))
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            row.AddView(Label(reminder.Title + "\n" + reminder.DueLocal.ToString("dd MMM, HH:mm"), 13), new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(ActionButton("Done", (_, _) =>
            {
                brain.RemoveReminder(reminder.Id);
                CancelReminder(reminder.Id);
                ShowReminders();
            }), new LinearLayout.LayoutParams(Dp(70), Dp(44)));
            panel.AddView(row);
        }
        new AlertDialog.Builder(this)
            .SetTitle("Reminders")
            .SetView(panel)
            .SetNegativeButton("Close", (s, e) => { })
            .SetNeutralButton("Quick timer", (s, e) => CreateTimer())
            .SetPositiveButton("Add reminder", (s, e) => CreateReminder())
            .Show();
    }

    private void CreateTimer()
    {
        var input = new EditText(this) { Hint = "Minutes", InputType = Android.Text.InputTypes.ClassNumber };
        new AlertDialog.Builder(this)
            .SetTitle("Quick timer")
            .SetMessage("How many minutes?")
            .SetView(input)
            .SetNegativeButton("Cancel", (s, e) => { })
            .SetPositiveButton("Start", (s, e) =>
            {
                if (!int.TryParse(input.Text, out int minutes) || minutes < 1 || minutes > 10080)
                {
                    AddMessage("assistant", "Give me a timer between 1 minute and 7 days, choom.");
                    return;
                }
                int id = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue);
                var reminder = new MobileReminder
                {
                    Id = id,
                    Title = "Your " + minutes + "-minute timer is up",
                    DueLocal = DateTime.Now.AddMinutes(minutes)
                };
                brain.AddReminder(reminder);
                ScheduleReminder(reminder);
                AddMessage("assistant", "Timer's running. I'll give you a nudge in " + minutes + " minutes.");
            })
            .Show();
    }

    private void CreateReminder()
    {
        var input = new EditText(this) { Hint = "What should I remind you about?" };
        new AlertDialog.Builder(this)
            .SetTitle("New reminder")
            .SetView(input)
            .SetNegativeButton("Cancel", (s, e) => { })
            .SetPositiveButton("Choose time", (s, e) =>
            {
                DateTime now = DateTime.Now.AddMinutes(5);
                new DatePickerDialog(this, (sender, dateArgs) =>
                {
                    int year = dateArgs.Year;
                    int month = dateArgs.Month;
                    int day = dateArgs.DayOfMonth;
                    new TimePickerDialog(this, (timeSender, timeArgs) =>
                    {
                        var due = new DateTime(year, month + 1, day, timeArgs.HourOfDay, timeArgs.Minute, 0);
                        if (due <= DateTime.Now)
                        {
                            AddMessage("assistant", "That time's already passed, choom. Pick a future time.");
                            return;
                        }
                        int id = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue);
                        var reminder = new MobileReminder { Id = id, Title = input.Text?.Trim() ?? "Reminder", DueLocal = due };
                        brain.AddReminder(reminder);
                        ScheduleReminder(reminder);
                        AddMessage("assistant", "Got it. I'll nudge you when it's time.");
                    }, now.Hour, now.Minute, true).Show();
                }, now.Year, now.Month - 1, now.Day).Show();
            })
            .Show();
    }

    private void ScheduleReminder(MobileReminder reminder)
    {
        var intent = new Intent(this, typeof(ReminderReceiver));
        intent.PutExtra("id", reminder.Id);
        intent.PutExtra("title", reminder.Title);
        var pending = PendingIntent.GetBroadcast(this, reminder.Id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var alarm = (AlarmManager)GetSystemService(AlarmService);
        alarm.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, new DateTimeOffset(reminder.DueLocal).ToUnixTimeMilliseconds(), pending);
    }

    private void CancelReminder(int id)
    {
        var intent = new Intent(this, typeof(ReminderReceiver));
        var pending = PendingIntent.GetBroadcast(this, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var alarm = (AlarmManager)GetSystemService(AlarmService);
        alarm.Cancel(pending);
        pending.Cancel();
    }

    private void ShowPhoneTools()
    {
        string[] options = { "Open browser", "Open camera", "Open device settings", "Search the web", "Copy last Capri reply", "Share last Capri reply" };
        new AlertDialog.Builder(this)
            .SetTitle("Phone tools")
            .SetItems(options, (sender, args) =>
            {
                switch (args.Which)
                {
                    case 0:
                        StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://www.google.com")));
                        break;
                    case 1:
                        try { StartActivity(new Intent(Android.Provider.MediaStore.ActionImageCapture)); }
                        catch { AddMessage("assistant", "This phone doesn't have a camera app I can open."); }
                        break;
                    case 2:
                        StartActivity(new Intent(Android.Provider.Settings.ActionSettings));
                        break;
                    case 3:
                        SearchWeb();
                        break;
                    case 4:
                        CopyLastReply();
                        break;
                    case 5:
                        ShareLastReply();
                        break;
                }
            })
            .Show();
    }

    private void SearchWeb()
    {
        var input = new EditText(this) { Hint = "What should I search for?" };
        new AlertDialog.Builder(this)
            .SetTitle("Search the web")
            .SetView(input)
            .SetNegativeButton("Cancel", (s, e) => { })
            .SetPositiveButton("Search", (s, e) =>
            {
                string query = input.Text?.Trim() ?? "";
                if (query.Length > 0)
                    StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://www.google.com/search?q=" + Uri.EscapeDataString(query))));
            })
            .Show();
    }

    private void CopyLastReply()
    {
        if (string.IsNullOrWhiteSpace(lastCapriReply)) return;
        var clipboard = (Android.Content.ClipboardManager)GetSystemService(ClipboardService);
        clipboard.PrimaryClip = ClipData.NewPlainText("Capri", lastCapriReply);
        Toast.MakeText(this, "Copied Capri's reply", ToastLength.Short).Show();
    }

    private void ShareLastReply()
    {
        if (string.IsNullOrWhiteSpace(lastCapriReply))
        {
            AddMessage("assistant", "Nothing to share yet. Give me something worth quoting.");
            return;
        }
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraText, lastCapriReply);
        StartActivity(Intent.CreateChooser(send, "Share Capri's message"));
    }

    protected override void OnDestroy()
    {
        speech?.Stop();
        speech?.Shutdown();
        base.OnDestroy();
    }
}

internal static class ViewExtensions
{
    internal static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
