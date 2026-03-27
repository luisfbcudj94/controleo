using System.Globalization;

namespace Controleo.Mobile.Services;

/// <summary>
/// Auto-formats numeric entry fields with thousand-dot separators (e.g., 1.250.000).
/// Attach via MoneyFormatHelper.Attach(entry) in code-behind.
/// </summary>
public static class MoneyFormatHelper
{
    private const int MaxDigits = 12;
    private const decimal MaxAmount = 999_999_999_999m;

    /// <summary>
    /// Attaches auto-formatting to an Entry. Digits-only, adds dots every 3 digits.
    /// </summary>
    public static void Attach(Entry entry)
    {
        var isFormatting = false;

        entry.TextChanged += (_, e) =>
        {
            if (isFormatting) return;

            var raw = e.NewTextValue ?? string.Empty;
            var digits = new string(raw.Where(char.IsDigit).ToArray());

            if (string.IsNullOrWhiteSpace(digits))
            {
                if (!string.IsNullOrEmpty(raw))
                {
                    isFormatting = true;
                    entry.Dispatcher.Dispatch(() =>
                    {
                        isFormatting = true;
                        try { entry.Text = string.Empty; }
                        finally { isFormatting = false; }
                    });
                }
                return;
            }

            if (digits.Length > MaxDigits)
                digits = digits[..MaxDigits];

            if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > MaxAmount)
                digits = ((long)MaxAmount).ToString(CultureInfo.InvariantCulture);

            var formatted = FormatWithDots(digits);
            if (string.Equals(formatted, raw, StringComparison.Ordinal)) return;

            entry.Dispatcher.Dispatch(() =>
            {
                if (isFormatting) return;
                isFormatting = true;
                try { entry.Text = formatted; }
                finally { isFormatting = false; }
            });
        };
    }

    /// <summary>Parse a formatted value back to decimal (strips dots).</summary>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(digits) &&
            decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            amount = parsed;
            return true;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount);
    }

    /// <summary>Format raw digits with dots every 3 from right.</summary>
    public static string FormatWithDots(string digits)
    {
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;

        var clean = digits.TrimStart('0');
        if (string.IsNullOrEmpty(clean)) clean = "0";

        var chars = new List<char>(clean.Length + clean.Length / 3);
        var count = 0;
        for (var i = clean.Length - 1; i >= 0; i--)
        {
            chars.Add(clean[i]);
            count++;
            if (count % 3 == 0 && i > 0) chars.Add('.');
        }
        chars.Reverse();
        return new string(chars.ToArray());
    }

    /// <summary>Set a decimal value into a formatted entry.</summary>
    public static void SetValue(Entry entry, decimal value)
    {
        entry.Text = value > 0 ? FormatWithDots(((long)value).ToString()) : string.Empty;
    }
}
