using Android.App;
using Android.Content;
using Android.OS;

namespace Capri.Mobile;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        string title = intent?.GetStringExtra("title") ?? "Something you asked me to remind you about.";
        int id = intent?.GetIntExtra("id", 1) ?? 1;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService);
        Notification notification;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel("capri-reminders", "Capri reminders", NotificationImportance.Default)
            {
                Description = "Reminders from Capri"
            };
            manager.CreateNotificationChannel(channel);
            notification = new Notification.Builder(context, "capri-reminders")
                .SetContentTitle("Capri's got a reminder")
                .SetContentText(title)
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetAutoCancel(true)
                .Build();
        }
        else
        {
            notification = new Notification.Builder(context)
                .SetContentTitle("Capri's got a reminder")
                .SetContentText(title)
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetAutoCancel(true)
                .Build();
        }

        manager.Notify(id, notification);
    }
}
