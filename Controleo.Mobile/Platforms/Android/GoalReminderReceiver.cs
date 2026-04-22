#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;

namespace Controleo.Mobile.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class GoalReminderReceiver : BroadcastReceiver
{
    public const string ActionReminder = "controleo.mobile.action.GOAL_REMINDER";
    public const string ChannelId = "goal-reminders";
    public const string ChannelName = "Recordatorios de metas";
    public const string ExtraNotificationId = "notification_id";
    public const string ExtraTitle = "title";
    public const string ExtraBody = "body";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        if (!string.Equals(intent.Action, ActionReminder, StringComparison.Ordinal)) return;

        var notificationId = intent.GetIntExtra(ExtraNotificationId, 2001);
        var title = intent.GetStringExtra(ExtraTitle) ?? "🎯 Meta de ahorro";
        var body = intent.GetStringExtra(ExtraBody) ?? "¡Recuerda aportar a tus metas de ahorro!";

        var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (notificationManager is null) return;

        EnsureNotificationChannel(notificationManager);

        PendingIntent? contentIntent = null;
        var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
        if (launchIntent is not null)
        {
            launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                pendingFlags |= PendingIntentFlags.Immutable;
            contentIntent = PendingIntent.GetActivity(context, notificationId + 1, launchIntent, pendingFlags);
        }

        Notification.Builder builder;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            builder = new Notification.Builder(context, ChannelId);
        else
        {
            builder = new Notification.Builder(context);
            builder.SetPriority((int)NotificationPriority.High);
        }

        builder
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetStyle(new Notification.BigTextStyle().BigText(body))
            .SetAutoCancel(true)
            .SetVisibility(NotificationVisibility.Public);

        if (contentIntent is not null)
            builder.SetContentIntent(contentIntent);

        notificationManager.Notify(notificationId, builder.Build());
    }

    private static void EnsureNotificationChannel(NotificationManager manager)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        if (manager.GetNotificationChannel(ChannelId) is not null) return;

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
        {
            Description = "Recordatorios motivacionales para tus metas de ahorro premium."
        };
        manager.CreateNotificationChannel(channel);
    }
}
#endif
