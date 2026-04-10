using System.Globalization;
using System.Net;
using System.Text;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosCatalogRepository(CosmosContainerProvider p) : ICatalogRepository
{
    private static readonly string[] MovementColors =
    [
        "#B6E6BD",
        "#A8D8F0",
        "#F8C4B4",
        "#C5B3E6",
        "#F4A9A8",
        "#F9E07F",
        "#7ECEC1",
        "#F0B5D0",
        "#A0C4A8",
        "#B0D4F1",
        "#E6C88C",
        "#D4A5C9",
        "#8CC5B2",
        "#F5D08A",
        "#9DB8D4",
        "#E8A89C",
        "#B3D9A3",
        "#C9A0D4",
        "#A8D4C8",
        "#F2C6A0",
    ];

    private static readonly string[] MovementIcons =
    [
        "🛒",
        "🏠",
        "🎉",
        "⚡",
        "📱",
        "💳",
        "🏦",
        "🧘",
        "✈️",
        "🔨",
        "🍕",
        "🚗",
        "🏥",
        "🎓",
        "🎬",
        "🐾",
        "👕",
        "🛠️",
        "🎁",
        "📚",
    ];

    private static readonly string[] PaymentIcons =
    [
        "💵",
        "💳",
        "🖤",
        "🧡",
        "💜",
        "💚",
        "🏦",
        "📲",
        "🔁",
        "🪙",
        "🏧",
        "📟",
        "🧾",
        "💻",
        "⌚",
        "📱",
        "🎟️",
        "🛍️",
        "🏢",
        "🤝",
    ];

    private static readonly HashSet<string> InvalidIcons = new(StringComparer.Ordinal)
    {
        string.Empty,
        "?",
        "??",
        "�",
    };

    private static readonly Dictionary<string, string> IconAliases = new(StringComparer.Ordinal)
    {
        ["✈"] = "✈️",
        ["🛠"] = "🛠️",
        ["🎟"] = "🎟️",
        ["🛍"] = "🛍️",
    };

    private static readonly HashSet<string> MovementIconSet = new(MovementIcons, StringComparer.Ordinal);
    private static readonly HashSet<string> PaymentIconSet = new(PaymentIcons, StringComparer.Ordinal);

    public async Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken ct)
    {
        var docId = BuildDocId(userId);

        try
        {
            var response = await p.Settings.ReadItemAsync<CatalogDocument>(docId, new PartitionKey(docId), cancellationToken: ct);
            var resource = response.Resource;

            var movementTypes = CosmosHelper.NormalizeCatalog(resource.MovementTypes);
            if (movementTypes.Length == 0)
            {
                movementTypes = CosmosHelper.NormalizeCatalog(p.Options.MovementTypes);
            }

            var paymentMethods = CosmosHelper.NormalizeCatalog(resource.PaymentMethods);
            if (paymentMethods.Length == 0)
            {
                paymentMethods = CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods);
            }

            var movementConfigs = BuildMovementConfigs(movementTypes, resource.MovementTypeConfigs ?? []);
            var paymentConfigs = BuildPaymentConfigs(paymentMethods, resource.PaymentMethodConfigs ?? []);

            return new ExpenseCatalog(movementTypes, paymentMethods, movementConfigs, paymentConfigs);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ExpenseCatalog(
                CosmosHelper.NormalizeCatalog(p.Options.MovementTypes),
                CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods));
        }
        catch
        {
            return new ExpenseCatalog(
                CosmosHelper.NormalizeCatalog(p.Options.MovementTypes),
                CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods));
        }
    }

    public async Task<OperationResult> UpdateCatalogsAsync(
        string userId,
        IReadOnlyList<string> movementTypes,
        IReadOnlyList<string> paymentMethods,
        IReadOnlyList<MovementTypeConfig>? movementTypeConfigs,
        IReadOnlyList<PaymentMethodConfig>? paymentMethodConfigs,
        CancellationToken ct)
    {
        var mt = CosmosHelper.NormalizeCatalog(movementTypes);
        var pm = CosmosHelper.NormalizeCatalog(paymentMethods);

        if (mt.Length == 0)
        {
            return new OperationResult(false, "Debe existir al menos una sección.");
        }

        if (pm.Length == 0)
        {
            return new OperationResult(false, "Debe existir al menos un medio de pago.");
        }

        try
        {
            var docId = BuildDocId(userId);
            CatalogDocument? existing = null;

            try
            {
                existing = (await p.Settings.ReadItemAsync<CatalogDocument>(docId, new PartitionKey(docId), cancellationToken: ct)).Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                existing = null;
            }

            var incomingMovementConfigs = movementTypeConfigs is null
                ? (existing?.MovementTypeConfigs ?? [])
                : movementTypeConfigs
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => new MovementTypeConfigDoc
                    {
                        Name = c.Name.Trim(),
                        Icon = c.Icon,
                        Color = c.Color,
                    })
                    .ToArray();

            var incomingPaymentConfigs = paymentMethodConfigs is null
                ? (existing?.PaymentMethodConfigs ?? [])
                : paymentMethodConfigs
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => new PaymentMethodConfigDoc
                    {
                        Name = c.Name.Trim(),
                        Icon = c.Icon,
                        IsCredit = c.IsCredit,
                        DefaultInstallments = c.DefaultInstallments,
                        DueDayOfMonth = c.DueDayOfMonth,
                        ReminderDaysBefore = c.ReminderDaysBefore,
                    })
                    .ToArray();

            var resolvedMovementConfigs = mt
                .Select(name =>
                {
                    var existingConfig = incomingMovementConfigs.FirstOrDefault(cfg => string.Equals(cfg.Name, name, StringComparison.OrdinalIgnoreCase));
                    return new MovementTypeConfigDoc
                    {
                        Name = name,
                        Icon = SanitizeMovementIcon(existingConfig?.Icon) ?? ResolveMovementIcon(name),
                        Color = string.IsNullOrWhiteSpace(existingConfig?.Color)
                            ? ResolveMovementColor(name)
                            : existingConfig!.Color.Trim(),
                    };
                })
                .ToArray();

            var resolvedPaymentConfigs = pm
                .Select(name =>
                {
                    var existingConfig = incomingPaymentConfigs.FirstOrDefault(cfg => string.Equals(cfg.Name, name, StringComparison.OrdinalIgnoreCase));
                    var normalizedConfig = NormalizePaymentConfig(existingConfig ?? new PaymentMethodConfigDoc { Name = name });
                    return new PaymentMethodConfigDoc
                    {
                        Name = name,
                        Icon = SanitizePaymentIcon(existingConfig?.Icon) ?? ResolvePaymentIcon(name),
                        IsCredit = normalizedConfig.IsCredit,
                        DefaultInstallments = normalizedConfig.DefaultInstallments,
                        DueDayOfMonth = normalizedConfig.DueDayOfMonth,
                        ReminderDaysBefore = normalizedConfig.ReminderDaysBefore,
                    };
                })
                .ToArray();

            var doc = new CatalogDocument
            {
                Id = docId,
                UserId = userId,
                MovementTypes = mt,
                PaymentMethods = pm,
                MovementTypeConfigs = resolvedMovementConfigs,
                PaymentMethodConfigs = resolvedPaymentConfigs,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };

            await p.Settings.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
            return new OperationResult(true, "Catálogos actualizados.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    private static string BuildDocId(string userId)
    {
        return $"{userId.Trim().Replace("/", "_").ToLowerInvariant()}-catalogs";
    }

    private static IReadOnlyList<MovementTypeConfig> BuildMovementConfigs(
        IReadOnlyList<string> movementTypes,
        IReadOnlyList<MovementTypeConfigDoc> source)
    {
        return movementTypes
            .Select(name =>
            {
                var config = source.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                return new MovementTypeConfig(
                    name,
                    SanitizeMovementIcon(config?.Icon) ?? ResolveMovementIcon(name),
                    string.IsNullOrWhiteSpace(config?.Color) ? ResolveMovementColor(name) : config!.Color.Trim());
            })
            .ToList();
    }

    private static IReadOnlyList<PaymentMethodConfig> BuildPaymentConfigs(
        IReadOnlyList<string> paymentMethods,
        IReadOnlyList<PaymentMethodConfigDoc> source)
    {
        return paymentMethods
            .Select(name =>
            {
                var config = source.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                var normalizedConfig = NormalizePaymentConfig(config ?? new PaymentMethodConfigDoc { Name = name });
                return new PaymentMethodConfig(
                    name,
                    SanitizePaymentIcon(config?.Icon) ?? ResolvePaymentIcon(name),
                    normalizedConfig.IsCredit,
                    normalizedConfig.DefaultInstallments,
                    normalizedConfig.DueDayOfMonth,
                    normalizedConfig.ReminderDaysBefore);
            })
            .ToList();
    }

    private static PaymentMethodConfigDoc NormalizePaymentConfig(PaymentMethodConfigDoc config)
    {
        var normalizedInstallments = NormalizeInstallments(config.DefaultInstallments);
        var isCredit = config.IsCredit;

        if (!isCredit && normalizedInstallments is > 1)
        {
            isCredit = true;
        }

        return new PaymentMethodConfigDoc
        {
            Name = config.Name,
            Icon = config.Icon,
            IsCredit = isCredit,
            DefaultInstallments = isCredit ? normalizedInstallments : null,
            DueDayOfMonth = NormalizeDayOfMonth(config.DueDayOfMonth),
            ReminderDaysBefore = NormalizeReminderDays(config.ReminderDaysBefore),
        };
    }

    private static int? NormalizeInstallments(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 1, 120);
    }

    private static int? NormalizeDayOfMonth(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 1, 31);
    }

    private static int? NormalizeReminderDays(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 0, 30);
    }

    private static string ResolveMovementColor(string movementType)
    {
        return MovementColors[StableCatalogIndex(movementType, MovementColors.Length)];
    }

    private static string ResolveMovementIcon(string movementType)
    {
        var normalized = NormalizeCatalogKey(movementType);
        if (normalized.Contains("basico", StringComparison.Ordinal)) return "🛒";
        if (normalized.Contains("hogar", StringComparison.Ordinal)) return "🏠";
        if (normalized.Contains("salida", StringComparison.Ordinal)) return "🎉";
        if (normalized.Contains("imprevisto", StringComparison.Ordinal)) return "⚡";
        if (normalized.Contains("suscrip", StringComparison.Ordinal)) return "📱";
        if (normalized.Contains("deuda", StringComparison.Ordinal)) return "💳";
        if (normalized.Contains("prestamo", StringComparison.Ordinal) || normalized.Contains("banco", StringComparison.Ordinal)) return "🏦";
        if (normalized.Contains("bienestar", StringComparison.Ordinal)) return "🧘";
        if (normalized.Contains("viaje", StringComparison.Ordinal)) return "✈️";
        if (normalized.Contains("obra", StringComparison.Ordinal)) return "🔨";
        if (normalized.Contains("comida", StringComparison.Ordinal)) return "🍕";

        return MovementIcons[StableCatalogIndex(movementType, MovementIcons.Length)];
    }

    private static string ResolvePaymentIcon(string paymentMethod)
    {
        var normalized = NormalizeCatalogKey(paymentMethod);
        if (normalized == "efectivo") return "💵";
        if (normalized == "transferencia") return "🔁";
        if (normalized == "nequi") return "📲";
        if (normalized == "tc black") return "🖤";
        if (normalized == "tc rappi") return "🧡";
        if (normalized == "tc nu") return "💜";
        if (normalized == "td bancolombia" || normalized == "bancolombia") return "🏦";

        return PaymentIcons[StableCatalogIndex(paymentMethod, PaymentIcons.Length)];
    }

    private static string? SanitizeMovementIcon(string? icon)
    {
        return SanitizeIcon(icon, MovementIconSet);
    }

    private static string? SanitizePaymentIcon(string? icon)
    {
        return SanitizeIcon(icon, PaymentIconSet);
    }

    private static string? SanitizeIcon(string? icon, HashSet<string> allowedSet)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var trimmed = icon.Trim();
        if (InvalidIcons.Contains(trimmed))
        {
            return null;
        }

        if (IconAliases.TryGetValue(trimmed, out var canonical))
        {
            trimmed = canonical;
        }

        return allowedSet.Contains(trimmed) ? trimmed : null;
    }

    private static int StableCatalogIndex(string value, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        uint hash = 2166136261;
        foreach (var ch in NormalizeCatalogKey(value))
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return (int)(hash % (uint)length);
    }

    private static string NormalizeCatalogKey(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                sb.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            sb.Append(ch);
            previousWasWhitespace = false;
        }

        return sb.ToString().Trim();
    }
}
