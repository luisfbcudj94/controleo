using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Controleo.Mobile.Platforms.Android;
using Microsoft.Maui.ApplicationModel;
#endif

namespace Controleo.Mobile.Core.Services;

public sealed class GoalNotificationService : IGoalNotificationService
{
#if ANDROID
    private const string RequestCodesPreferenceKey = "goal.reminder.request-codes.v1";

    private static readonly string[] HighPriorityMessages =
    [
        "💪 ¡Tú puedes! Hoy es un gran día para aportar a '{name}'.",
        "🔥 ¡No pares! Llevas {percent}% de '{name}'. ¡Sigue así!",
        "🎯 Tu meta '{name}' te espera. ¡Cada peso cuenta!",
        "🚀 ¡Un pequeño aporte hoy, un gran logro mañana! Aporta a '{name}'.",
        "⭐ ¡Eres imparable! ${suggested}/mes te acerca a '{name}'.",
        "💎 Tu disciplina es tu superpoder. ¡Aporta hoy a '{name}'!"
    ];

    private static readonly string[] MediumPriorityMessages =
    [
        "🌟 Recuerda tu meta '{name}'. ¡Cada aporte suma!",
        "💰 ¿Ya aportaste este mes a '{name}'? ¡Tú decides tu futuro!",
        "📈 Llevas {percent}% de '{name}'. ¡Vas por buen camino!",
        "🌱 Tu ahorro crece poco a poco. ¡Aporta a '{name}'!",
        "✨ Con ~${suggested}/mes alcanzas '{name}'. ¡Hazlo realidad!"
    ];

    private static readonly string[] LowPriorityMessages =
    [
        "🏦 No olvides tu meta '{name}'. ¡Un aporte al mes hace la diferencia!",
        "🌈 '{name}' sigue ahí esperándote. ¿Le aportas hoy?",
        "💚 Recuerda: ahorrar es cuidarte. Tu meta '{name}' te lo agradecerá."
    ];
#endif

    public async Task EnsurePermissionAsync(bool isPremium, CancellationToken cancellationToken)
    {
#if ANDROID
        cancellationToken.ThrowIfCancellationRequested();

        if (!isPremium)
        {
            CancelAllScheduledReminders();
            return;
        }

        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task SyncGoalRemindersAsync(bool isPremium, IReadOnlyList<SavingsGoalItem> goals, CancellationToken cancellationToken)
    {
#if ANDROID
        cancellationToken.ThrowIfCancellationRequested();

        var activeGoals = goals.Where(g => g.Status == "Active").ToList();

        if (!isPremium || activeGoals.Count == 0)
        {
            CancelAllScheduledReminders();
            return;
        }

        if (!await HasNotificationPermissionAsync()) return;

        var now = DateTimeOffset.Now;
        var reminders = BuildGoalReminders(activeGoals, now)
            .OrderBy(r => r.TriggerAt)
            .ToArray();

        ReplaceScheduledReminders(reminders);
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static async Task<bool> HasNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return true;
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    private static IEnumerable<GoalReminderItem> BuildGoalReminders(
        IReadOnlyList<SavingsGoalItem> goals,
        DateTimeOffset now)
    {
        var seen = new HashSet<int>();

        foreach (var goal in goals)
        {
            var remindersPerMonth = goal.Priority switch
            {
                "Alta" => 6,
                "Media" => 3,
                _ => 1 // Baja
            };

            var messages = goal.Priority switch
            {
                "Alta" => HighPriorityMessages,
                "Media" => MediumPriorityMessages,
                _ => LowPriorityMessages
            };

            // Spread reminders across the current AND next month
            for (var monthOffset = 0; monthOffset <= 1; monthOffset++)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1, 9, 0, 0, DateTimeKind.Local).AddMonths(monthOffset);
                var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                var spacing = Math.Max(1, daysInMonth / remindersPerMonth);

                for (var i = 0; i < remindersPerMonth; i++)
                {
                    var dayOfMonth = Math.Min(1 + (i * spacing) + StableDayOffset(goal.Id, i), daysInMonth);
                    var triggerDate = new DateTime(monthStart.Year, monthStart.Month, dayOfMonth, 9, 0, 0, DateTimeKind.Local);
                    var triggerAt = new DateTimeOffset(triggerDate);

                    if (triggerAt <= now.AddMinutes(1)) continue;

                    var requestCode = StableRequestCode($"goal|{goal.Id}|{triggerAt:yyyyMMdd}");
                    if (!seen.Add(requestCode)) continue;

                    var template = messages[i % messages.Length];
                    var body = template
                        .Replace("{name}", goal.Name)
                        .Replace("{percent}", $"{goal.ProgressPercent:F0}")
                        .Replace("{suggested}", $"{goal.SuggestedMonthlyContribution:N0}");

                    yield return new GoalReminderItem(requestCode, triggerAt, "🎯 Meta de ahorro", body);
                }
            }
        }
    }

    /// <summary>Deterministic small offset (0-2 days) per goal+index to avoid all goals firing same day.</summary>
    private static int StableDayOffset(string goalId, int index)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in goalId) hash = (hash ^ ch) * 16777619;
            hash = (hash ^ (uint)index) * 16777619;
            return (int)(hash % 3);
        }
    }

    private static int StableRequestCode(string key)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key) hash = (hash ^ ch) * 16777619;
            var code = (int)(hash & 0x7FFFFFFF);
            return code == 0 ? 1 : code;
        }
    }

    private static void ReplaceScheduledReminders(IReadOnlyList<GoalReminderItem> reminders)
    {
        var context = Android.App.Application.Context;
        var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager is null) return;

        var previousCodes = LoadScheduledRequestCodes();
        foreach (var code in previousCodes)
            CancelReminder(alarmManager, context, code);

        var nextCodes = new List<int>(reminders.Count);
        foreach (var reminder in reminders)
        {
            ScheduleReminder(alarmManager, context, reminder);
            nextCodes.Add(reminder.RequestCode);
        }

        SaveScheduledRequestCodes(nextCodes);
    }

    private static void CancelAllScheduledReminders()
    {
        var context = Android.App.Application.Context;
        var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager is null)
        {
            SaveScheduledRequestCodes([]);
            return;
        }

        foreach (var code in LoadScheduledRequestCodes())
            CancelReminder(alarmManager, context, code);

        SaveScheduledRequestCodes([]);
    }

    private static void ScheduleReminder(AlarmManager alarmManager, Context context, GoalReminderItem reminder)
    {
        using var pendingIntent = CreatePendingIntent(context, reminder.RequestCode, reminder.Title, reminder.Body);
        if (pendingIntent is null) return;

        var triggerAtMillis = reminder.TriggerAt.ToUnixTimeMilliseconds();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
            alarmManager.SetExact(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
        else
            alarmManager.Set(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
    }

    private static void CancelReminder(AlarmManager alarmManager, Context context, int requestCode)
    {
        using var pendingIntent = CreatePendingIntent(context, requestCode, null, null);
        if (pendingIntent is null) return;
        alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }

    private static PendingIntent? CreatePendingIntent(Context context, int requestCode, string? title, string? body)
    {
        var intent = new Intent(context, typeof(GoalReminderReceiver));
        intent.SetAction(GoalReminderReceiver.ActionReminder);
        intent.PutExtra(GoalReminderReceiver.ExtraNotificationId, requestCode);
        if (!string.IsNullOrWhiteSpace(title)) intent.PutExtra(GoalReminderReceiver.ExtraTitle, title);
        if (!string.IsNullOrWhiteSpace(body)) intent.PutExtra(GoalReminderReceiver.ExtraBody, body);

        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M) flags |= PendingIntentFlags.Immutable;
        return PendingIntent.GetBroadcast(context, requestCode, intent, flags);
    }

    private static HashSet<int> LoadScheduledRequestCodes()
    {
        var raw = Preferences.Default.Get(RequestCodesPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 0)
            .Where(v => v > 0)
            .ToHashSet();
    }

    private static void SaveScheduledRequestCodes(IEnumerable<int> codes)
    {
        var serialized = string.Join(",", codes.Where(c => c > 0).Distinct().OrderBy(c => c)
            .Select(c => c.ToString(CultureInfo.InvariantCulture)));
        Preferences.Default.Set(RequestCodesPreferenceKey, serialized);
    }

    private sealed record GoalReminderItem(int RequestCode, DateTimeOffset TriggerAt, string Title, string Body);
#endif
}
