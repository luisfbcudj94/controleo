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

public sealed class ObligationNotificationService : IObligationNotificationService
{
#if ANDROID
    private const string RequestCodesPreferenceKey = "obligation.reminder.request-codes.v1";
    private static readonly CultureInfo EsCulture = new("es-CO");
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

        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
#else
        await Task.CompletedTask;
#endif
    }

    public async Task SyncReminderPlanAsync(bool isPremium, IReadOnlyList<ObligationItem> obligations, CancellationToken cancellationToken)
    {
#if ANDROID
        cancellationToken.ThrowIfCancellationRequested();

        if (!isPremium || obligations.Count == 0)
        {
            CancelAllScheduledReminders();
            return;
        }

        if (!await HasNotificationPermissionAsync())
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var reminders = BuildUpcomingReminders(obligations, now)
            .OrderBy(item => item.TriggerAt)
            .ToArray();

        ReplaceScheduledReminders(reminders);
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static async Task<bool> HasNotificationPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    private static IEnumerable<ReminderPlanItem> BuildUpcomingReminders(
        IEnumerable<ObligationItem> obligations,
        DateTimeOffset now)
    {
        var seen = new HashSet<int>();

        foreach (var obligation in obligations)
        {
            if (!obligation.IsActive || string.IsNullOrWhiteSpace(obligation.Id))
            {
                continue;
            }

            var reminderDays = Math.Clamp(obligation.ReminderDaysBefore, 0, 30);
            var safeDayOfMonth = Math.Clamp(obligation.DueDayOfMonth, 1, 31);

            for (var monthOffset = 0; monthOffset <= 1; monthOffset++)
            {
                var monthStart = new DateTime(now.Year, now.Month, 1, 9, 0, 0, DateTimeKind.Local).AddMonths(monthOffset);
                var maxDay = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                var dueDateTime = new DateTime(
                    monthStart.Year,
                    monthStart.Month,
                    Math.Min(safeDayOfMonth, maxDay),
                    9,
                    0,
                    0,
                    DateTimeKind.Local);

                var triggerAt = new DateTimeOffset(dueDateTime).AddDays(-reminderDays);
                if (triggerAt <= now.AddMinutes(1))
                {
                    continue;
                }

                var requestCode = StableRequestCode($"{obligation.Id}|{triggerAt:yyyyMMdd}");
                if (!seen.Add(requestCode))
                {
                    continue;
                }

                var title = "Recordatorio de pago";
                var body = $"{obligation.Description} vence el {dueDateTime.ToString("dd 'de' MMMM", EsCulture)}.";
                yield return new ReminderPlanItem(requestCode, triggerAt, title, body);
            }
        }
    }

    private static int StableRequestCode(string key)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash = (hash ^ ch) * 16777619;
            }

            var requestCode = (int)(hash & 0x7FFFFFFF);
            return requestCode == 0 ? 1 : requestCode;
        }
    }

    private static void ReplaceScheduledReminders(IReadOnlyList<ReminderPlanItem> reminders)
    {
        var context = Android.App.Application.Context;
        var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarmManager is null)
        {
            return;
        }

        var previousCodes = LoadScheduledRequestCodes();
        foreach (var requestCode in previousCodes)
        {
            CancelReminder(alarmManager, context, requestCode);
        }

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

        foreach (var requestCode in LoadScheduledRequestCodes())
        {
            CancelReminder(alarmManager, context, requestCode);
        }

        SaveScheduledRequestCodes([]);
    }

    private static void ScheduleReminder(AlarmManager alarmManager, Context context, ReminderPlanItem reminder)
    {
        using var pendingIntent = CreateReminderPendingIntent(context, reminder.RequestCode, reminder.Title, reminder.Body);
        if (pendingIntent is null)
        {
            return;
        }

        var triggerAtMillis = reminder.TriggerAt.ToUnixTimeMilliseconds();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
        {
            alarmManager.SetExact(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
            return;
        }

        alarmManager.Set(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
    }

    private static void CancelReminder(AlarmManager alarmManager, Context context, int requestCode)
    {
        using var pendingIntent = CreateReminderPendingIntent(context, requestCode, null, null);
        if (pendingIntent is null)
        {
            return;
        }

        alarmManager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }

    private static PendingIntent? CreateReminderPendingIntent(Context context, int requestCode, string? title, string? body)
    {
        var intent = new Intent(context, typeof(ObligationReminderReceiver));
        intent.SetAction(ObligationReminderReceiver.ActionReminder);
        intent.PutExtra(ObligationReminderReceiver.ExtraNotificationId, requestCode);

        if (!string.IsNullOrWhiteSpace(title))
        {
            intent.PutExtra(ObligationReminderReceiver.ExtraTitle, title);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            intent.PutExtra(ObligationReminderReceiver.ExtraBody, body);
        }

        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return PendingIntent.GetBroadcast(context, requestCode, intent, flags);
    }

    private static HashSet<int> LoadScheduledRequestCodes()
    {
        var raw = Preferences.Default.Get(RequestCodesPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new HashSet<int>();
        foreach (var value in values)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static void SaveScheduledRequestCodes(IEnumerable<int> requestCodes)
    {
        var serialized = string.Join(
            ",",
            requestCodes
                .Where(item => item > 0)
                .Distinct()
                .OrderBy(item => item)
                .Select(item => item.ToString(CultureInfo.InvariantCulture)));

        Preferences.Default.Set(RequestCodesPreferenceKey, serialized);
    }

    private sealed record ReminderPlanItem(int RequestCode, DateTimeOffset TriggerAt, string Title, string Body);
#endif
}
