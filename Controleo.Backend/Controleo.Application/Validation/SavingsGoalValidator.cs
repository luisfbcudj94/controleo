using Controleo.Application.DTOs;
namespace Controleo.Application.Validation;
public static class SavingsGoalValidator
{
    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase) { "Alta", "Media", "Baja" };
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase) { "Active", "Completed", "Cancelled" };

    public static Dictionary<string, string[]> Validate(GoalUpsertRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) e["name"] = ["El nombre de la meta es requerido."];
        else if (r.Name.Trim().Length > 100) e["name"] = ["El nombre no puede superar 100 caracteres."];
        if (string.IsNullOrWhiteSpace(r.Icon)) e["icon"] = ["El icono es requerido."];
        if (r.TargetAmount <= 0) e["targetAmount"] = ["El monto objetivo debe ser mayor a cero."];
        if (r.TargetDate == default) e["targetDate"] = ["La fecha objetivo es requerida."];
        if (!string.IsNullOrWhiteSpace(r.Priority) && !ValidPriorities.Contains(r.Priority.Trim()))
            e["priority"] = ["Prioridad debe ser Alta, Media o Baja."];
        if (r.Status is not null && !ValidStatuses.Contains(r.Status.Trim()))
            e["status"] = ["Estado debe ser Active, Completed o Cancelled."];
        return e;
    }

    public static Dictionary<string, string[]> ValidateContribution(GoalContributionRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (r.Amount <= 0) e["amount"] = ["El monto del aporte debe ser mayor a cero."];
        if (r.Date == default) e["date"] = ["La fecha del aporte es requerida."];
        if (r.Note is not null && r.Note.Trim().Length > 200) e["note"] = ["La nota no puede superar 200 caracteres."];
        return e;
    }

    public static Dictionary<string, string[]> ValidateSimulation(GoalSimulationRequest r)
    {
        var e = new Dictionary<string, string[]>();
        var validScenarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ChangeAmount", "ChangeDate" };
        if (string.IsNullOrWhiteSpace(r.ScenarioType) || !validScenarios.Contains(r.ScenarioType.Trim()))
            e["scenarioType"] = ["Tipo de escenario debe ser ChangeAmount o ChangeDate."];
        if (r.NewValue <= 0) e["newValue"] = ["El valor debe ser mayor a cero."];
        return e;
    }
}
