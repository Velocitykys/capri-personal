using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;

namespace Capri.Mobile;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { "android.intent.action.BOOT_COMPLETED" })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        try
        {
            string file = Path.Combine(context.FilesDir.AbsolutePath, "capri-reminders.json");
            if (!File.Exists(file)) return;
            var reminders = JsonSerializer.Deserialize<List<MobileReminder>>(File.ReadAllText(file)) ?? new();
            var alarm = (AlarmManager)context.GetSystemService(Context.AlarmService);

            foreach (var reminder in reminders.Where(r => !r.Completed && r.DueLocal > DateTime.Now))
            {
                var reminderIntent = new Intent(context, typeof(ReminderReceiver));
                reminderIntent.PutExtra("id", reminder.Id);
                reminderIntent.PutExtra("title", reminder.Title);
                var pending = PendingIntent.GetBroadcast(
                    context, reminder.Id, reminderIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                alarm.SetExactAndAllowWhileIdle(
                    AlarmType.RtcWakeup,
                    new DateTimeOffset(reminder.DueLocal).ToUnixTimeMilliseconds(),
                    pending);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine("CAPRI REMINDER RESTORE FAILURE: " + error);
        }
    }
}
