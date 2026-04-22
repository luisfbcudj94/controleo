using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Application.Options;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace Controleo.Application.Services;

public sealed class MoneyCoachService(
    IChatRepository chatRepo,
    IExpenseRepository expenseRepo,
    IBudgetRepository budgetRepo,
    IObligationRepository obligationRepo,
    IUserProfileRepository profileRepo,
    ISavingsGoalRepository goalRepo,
    IOptions<LlmProviderOptions> llmOptions,
    IOptions<AiBudgetOptions> budgetOptions) : IMoneyCoachService
{
    private static readonly HttpClient SharedClient = new();
    private readonly LlmProviderOptions _llm = llmOptions.Value;
    private readonly AiBudgetOptions _budget = budgetOptions.Value;

    private const string SystemPrompt = """
        Eres "Coach IA", el asistente financiero personal de Controleo. Tu personalidad es amigable, motivadora y directa.

        REGLAS:
        - Responde SIEMPRE en español
        - Usa los datos financieros reales del usuario que vienen en el contexto
        - NUNCA inventes datos ni montos que no estén en el contexto
        - Si no tienes datos suficientes, dilo honestamente y sugiere al usuario registrarlos
        - Sé breve y concreto (máximo 3-4 párrafos)
        - Usa emojis con moderación para ser amigable (1-2 por respuesta)
        - Da consejos accionables con números concretos cuando sea posible
        - Motiva al usuario, celebra sus logros
        - Si el usuario pregunta algo fuera de finanzas, redirige amablemente al tema financiero
        """;

    public async Task<ChatMessageResponse> SendMessageAsync(string userId, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 500)
            return new ChatMessageResponse("assistant", "Por favor envía un mensaje entre 1 y 500 caracteres.", DateTimeOffset.UtcNow);

        var trimmedContent = content.Trim();

        // Save user message
        await chatRepo.SaveMessageAsync(userId, "user", trimmedContent, ct);

        // Build financial context
        var financialContext = await BuildFinancialContextAsync(userId, ct);

        // Get recent history for multi-turn
        var history = await chatRepo.GetHistoryAsync(userId, 10, ct);

        // Call LLM
        string assistantReply;
        try
        {
            var chatHistory = history.Select(m => new Dictionary<string, string> { ["role"] = m.Role, ["content"] = m.Content }).ToList<object>();
            assistantReply = await CallLlmAsync(financialContext, chatHistory, trimmedContent, ct);
        }
        catch
        {
            assistantReply = "Lo siento, tuve un problema al procesar tu consulta. Por favor intenta de nuevo en unos segundos. 🙏";
        }

        // Save assistant reply
        var now = DateTimeOffset.UtcNow;
        await chatRepo.SaveMessageAsync(userId, "assistant", assistantReply, ct);

        return new ChatMessageResponse("assistant", assistantReply, now);
    }

    public async Task<ChatHistoryResponse> GetHistoryAsync(string userId, int limit, CancellationToken ct)
    {
        var messages = await chatRepo.GetHistoryAsync(userId, limit, ct);
        return new ChatHistoryResponse(messages.Select(m => new ChatMessageResponse(m.Role, m.Content, m.CreatedAt)).ToArray());
    }

    public async Task<OperationResult> ClearHistoryAsync(string userId, CancellationToken ct)
    {
        await chatRepo.ClearHistoryAsync(userId, ct);
        return new OperationResult(true, "Historial eliminado.");
    }

    public async Task<CoachSuggestionsResponse> GetSuggestionsAsync(string userId, CancellationToken ct)
    {
        var suggestions = new List<string>
        {
            "¿En qué estoy gastando más este mes?",
            "¿Cómo puedo ahorrar más?",
            "¿Cuál es mi situación financiera?",
            "Dame tips para reducir gastos"
        };

        try
        {
            var budgets = await budgetRepo.GetBudgetsAsync(userId, ct);
            if (budgets.Count > 0)
            {
                suggestions.Add("¿Voy bien con mi presupuesto?");
                suggestions.Add("¿En qué categorías me estoy pasando?");
            }

            var obligations = await obligationRepo.GetObligationsAsync(userId, ct);
            if (obligations.Count > 0)
                suggestions.Add("¿Tengo deudas preocupantes?");

            var goals = await goalRepo.GetGoalsAsync(userId, ct);
            if (goals.Count > 0)
                suggestions.Add("¿Voy bien con mis metas de ahorro?");

            var profile = await profileRepo.GetAsync(userId, ct);
            if (profile?.MonthlyIncome is > 0)
                suggestions.Add("¿Cuánto debería ahorrar al mes?");
        }
        catch { /* Return base suggestions on error */ }

        return new CoachSuggestionsResponse(suggestions);
    }

    private async Task<string> BuildFinancialContextAsync(string userId, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DATOS FINANCIEROS DEL USUARIO (reales, no inventar) ===");

        try
        {
            // Profile
            var profile = await profileRepo.GetAsync(userId, ct);
            if (profile?.MonthlyIncome is > 0)
                sb.AppendLine($"Ingreso mensual: ${profile.MonthlyIncome.Value:N0}");
            else
                sb.AppendLine("Ingreso mensual: No configurado");

            // Expenses - current month with full detail, previous months summarized
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentMk = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var currentExpenses = await expenseRepo.GetExpensesAsync(userId, currentMk, ct);
            var currentTotal = currentExpenses.Sum(e => e.Amount);

            sb.AppendLine($"\n--- GASTOS ESTE MES ({currentMk}) total: ${currentTotal:N0} ---");
            foreach (var e in currentExpenses.OrderByDescending(x => x.Date).ThenByDescending(x => x.Amount).Take(40))
                sb.AppendLine($"  {e.Date:dd/MM} | {e.Description} | ${e.Amount:N0} | {e.MovementType} | {e.PaymentMethod}");

            // Previous 2 months - summarized by category
            for (int i = 1; i <= 2; i++)
            {
                var m = today.AddMonths(-i);
                var mk = m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var expenses = await expenseRepo.GetExpensesAsync(userId, mk, ct);
                if (expenses.Count == 0) continue;
                var monthTotal = expenses.Sum(e => e.Amount);
                sb.AppendLine($"\n--- MES {mk} total: ${monthTotal:N0} ({expenses.Count} gastos) ---");
                var byCategory = expenses.GroupBy(e => e.MovementType).OrderByDescending(g => g.Sum(x => x.Amount));
                foreach (var g in byCategory.Take(8))
                    sb.AppendLine($"  {g.Key}: ${g.Sum(x => x.Amount):N0} ({g.Count()} gastos)");
            }

            // Budgets
            var budgets = await budgetRepo.GetBudgetsAsync(userId, ct);
            if (budgets.Count > 0)
            {
                sb.AppendLine("Presupuestos configurados:");
                foreach (var b in budgets)
                    sb.AppendLine($"  - {b.MovementType}: ${b.Amount:N0}");
            }

            // Obligations
            var obligations = await obligationRepo.GetObligationsAsync(userId, ct);
            if (obligations.Count > 0)
            {
                var activeObl = obligations.Where(o => o.IsActive).ToList();
                var totalMonthly = activeObl.Sum(o => o.MonthlyPayment);
                sb.AppendLine($"Obligaciones activas: {activeObl.Count}, total mensual: ${totalMonthly:N0}");
                foreach (var o in activeObl.Take(5))
                    sb.AppendLine($"  - {o.Description}: ${o.MonthlyPayment:N0}/mes (día {o.DueDayOfMonth})");
            }

            // Goals
            var goals = await goalRepo.GetGoalsAsync(userId, ct);
            var activeGoals = goals.Where(g => g.Status == "Active").ToList();
            if (activeGoals.Count > 0)
            {
                sb.AppendLine($"Metas de ahorro activas: {activeGoals.Count}");
                foreach (var g in activeGoals.Take(5))
                    sb.AppendLine($"  - {g.Name}: ${g.CurrentAmount:N0}/${g.TargetAmount:N0} (vence {g.TargetDate:dd/MM/yyyy})");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(Error cargando algunos datos: {ex.Message})");
        }

        sb.AppendLine("=== FIN DATOS ===");
        return sb.ToString();
    }

    private async Task<string> CallLlmAsync(string financialContext, IReadOnlyList<object> history, string userMessage, CancellationToken ct)
    {
        var apiKey = ResolveApiKey(_llm.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey) || !_llm.Enabled)
            return GenerateFallbackResponse(userMessage);

        var messages = new List<object>
        {
            new { role = "system", content = SystemPrompt + "\n\n" + financialContext }
        };

        // Add conversation history (skip last user message, we add it fresh)
        foreach (var msg in history.SkipLast(1))
            messages.Add(msg);

        messages.Add(new { role = "user", content = userMessage });

        var payload = new
        {
            model = _llm.Model,
            temperature = 0.4,
            max_tokens = 500,
            messages
        };

        var baseUrl = string.IsNullOrWhiteSpace(_llm.ApiBaseUrl) ? "https://api.openai.com/v1/" : _llm.ApiBaseUrl.TrimEnd('/') + "/";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(_llm.RequestTimeoutSeconds, 10)));

        var response = await SharedClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? GenerateFallbackResponse(userMessage) : content.Trim();
    }

    private static string GenerateFallbackResponse(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("gasto") || lower.Contains("gastando"))
            return "📊 Revisa tu dashboard para ver en qué categorías estás gastando más. Te recomiendo establecer presupuestos por categoría para tener mejor control.";
        if (lower.Contains("ahorro") || lower.Contains("ahorrar"))
            return "💰 Un buen punto de partida es la regla 50/30/20: 50% necesidades, 30% deseos, 20% ahorro. Revisa tus gastos y busca categorías donde puedas reducir.";
        if (lower.Contains("presupuesto"))
            return "🎯 Ve a Presupuestos en el menú lateral para configurar topes por categoría. Así podrás ver en el dashboard cuándo te estás pasando.";
        if (lower.Contains("deuda") || lower.Contains("obligacion"))
            return "🏦 Revisa tus obligaciones en el menú. Prioriza pagar las de mayor interés primero (método avalancha) o las más pequeñas primero para motivarte (método bola de nieve).";
        return "🤖 Estoy aquí para ayudarte con tus finanzas. Prueba preguntándome sobre tus gastos, presupuesto, ahorro o deudas. ¡Con gusto te oriento!";
    }

    private static string ResolveApiKey(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && !configured.StartsWith("TU_") && !configured.StartsWith("CAMBIA"))
            return configured.Trim();
        var env = Environment.GetEnvironmentVariable("LlmProvider__ApiKey");
        return string.IsNullOrWhiteSpace(env) ? "" : env.Trim();
    }
}
