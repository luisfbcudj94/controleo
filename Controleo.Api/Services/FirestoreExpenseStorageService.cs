using Controleo.Api.Models;
using Controleo.Api.Options;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Services;

public sealed class FirestoreExpenseStorageService : IExpenseStorageService
{
    private readonly FirebaseStorageOptions _options;
    private readonly FirestoreDb _firestoreDb;

    public FirestoreExpenseStorageService(IOptions<FirebaseStorageOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ProjectId))
        {
            throw new InvalidOperationException("Debes configurar FirebaseStorage:ProjectId en appsettings.");
        }

        var builder = new FirestoreDbBuilder
        {
            ProjectId = _options.ProjectId
        };

        if (!string.IsNullOrWhiteSpace(_options.CredentialsFilePath))
        {
            builder.CredentialsPath = _options.CredentialsFilePath;
        }

        _firestoreDb = builder.Build();
    }

    public async Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        if (!IsAllowedValue(request.MovementType, _options.MovementTypes))
        {
            return new SaveExpenseResult(false, "Tipo de movimiento no permitido.", 0);
        }

        if (!IsAllowedValue(request.PaymentMethod, _options.PaymentMethods))
        {
            return new SaveExpenseResult(false, "Medio de pago no permitido.", 0);
        }

        try
        {
            var collection = _firestoreDb.Collection(_options.CollectionName);
            var payload = new Dictionary<string, object>
            {
                ["date"] = request.Date.ToString("yyyy-MM-dd"),
                ["description"] = request.Description.Trim(),
                ["amount"] = Convert.ToDouble(request.Amount),
                ["movementType"] = request.MovementType.Trim(),
                ["paymentMethod"] = request.PaymentMethod.Trim(),
                ["createdAt"] = Timestamp.GetCurrentTimestamp()
            };

            var documentReference = await collection.AddAsync(payload, cancellationToken);

            return new SaveExpenseResult(true, $"Gasto guardado en Firebase (id: {documentReference.Id}).", 0);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No se pudo guardar en Firebase: {ex.Message}", 0);
        }
    }

    private static bool IsAllowedValue(string value, IEnumerable<string> allowedValues)
    {
        var normalizedValue = value.Trim();
        return allowedValues.Any(item => string.Equals(item.Trim(), normalizedValue, StringComparison.OrdinalIgnoreCase));
    }
}
