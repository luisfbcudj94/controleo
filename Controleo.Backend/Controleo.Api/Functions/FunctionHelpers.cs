using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Functions;

/// <summary>
/// Shared HTTP helpers for Azure Functions — CORS, JSON responses, body parsing, auth.
/// Keeps the Function classes thin.
/// </summary>
internal static class FunctionHelpers
{
    public static async Task<(ApiUserContext? User, HttpResponseData? Response)> AuthorizeAsync(
        HttpRequestData request, IAccessTokenValidator validator, LocalAuthOptions authOptions, bool isDevelopment, CancellationToken ct)
    {
        var header = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;

        if (authOptions.AllowAnonymousInDevelopment && isDevelopment && string.IsNullOrWhiteSpace(header))
            return (new ApiUserContext("dev-user", "dev-user", "Dev User", "dev@controleo.local"), null);

        var validation = await validator.ValidateAsync(header, ct);
        if (!validation.IsValid || validation.User is null)
        {
            var err = await JsonAsync(request, HttpStatusCode.Unauthorized,
                new OperationResult(false, string.IsNullOrWhiteSpace(validation.ErrorMessage) ? "Unauthorized" : validation.ErrorMessage), ct);
            return (null, err);
        }
        return (validation.User, null);
    }

    public static async Task<HttpResponseData> JsonAsync(HttpRequestData request, HttpStatusCode statusCode, object payload, CancellationToken ct)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(payload, cancellationToken: ct);
        response.StatusCode = statusCode;
        AddCorsHeaders(response);
        return response;
    }

    public static HttpResponseData NoContent(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.NoContent);
        AddCorsHeaders(response);
        return response;
    }

    public static async Task<HttpResponseData> ValidationProblemAsync(HttpRequestData request, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var payload = new { type = "https://tools.ietf.org/html/rfc9110#section-15.5.1", title = "One or more validation errors occurred.", status = 400, errors };
        return await JsonAsync(request, HttpStatusCode.BadRequest, payload, ct);
    }

    public static void AddCorsHeaders(HttpResponseData response)
    {
        if (!response.Headers.TryGetValues("Access-Control-Allow-Origin", out _))
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        if (!response.Headers.TryGetValues("Access-Control-Allow-Headers", out _))
            response.Headers.Add("Access-Control-Allow-Headers", "authorization, content-type, x-requested-with");
        if (!response.Headers.TryGetValues("Access-Control-Allow-Methods", out _))
            response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
    }

    public static bool TryResolveMonth(string? month, out string monthKey)
    {
        if (string.IsNullOrWhiteSpace(month)) { monthKey = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture); return true; }
        if (DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pm))
        { monthKey = pm.ToString("yyyy-MM", CultureInfo.InvariantCulture); return true; }
        monthKey = ""; return false;
    }

    public static int NormalizePageSize(int value) => value switch { 10 => 10, 20 => 20, _ => 5 };

    public static Dictionary<string, string> ParseQuery(HttpRequestData request)
    {
        var parsed = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in parsed.AllKeys)
            if (!string.IsNullOrWhiteSpace(key)) result[key] = parsed[key] ?? "";
        return result;
    }

    // ── Body parsing ──

    public static async Task<T?> ReadBodyAsync<T>(HttpRequestData request, CancellationToken ct)
    {
        try
        {
            var raw = await ReadRawBodyAsync(request, ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var normalized = NormalizeRawPayload(raw);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    var parsed = TryDeserialize<T>(normalized);
                    if (parsed is not null) return parsed;
                    try { var root = JsonNode.Parse(normalized); if (root is JsonValue vn && vn.TryGetValue<string>(out var inner)) { var nested = TryDeserialize<T>(inner); if (nested is not null) return nested; } } catch { }
                }
            }
            if (request.Body.CanSeek) request.Body.Position = 0;
            return await request.ReadFromJsonAsync<T>(ct);
        }
        catch { return default; }
    }

    public static async Task<ExpenseEntryRequest?> ReadExpenseEntryRequestAsync(HttpRequestData request, CancellationToken ct)
    {
        var raw = await ReadRawBodyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var norm = NormalizeRawPayload(raw);
        if (string.IsNullOrWhiteSpace(norm)) return null;

        var direct = TryDeserialize<ExpenseEntryRequest>(norm);
        if (direct is not null) return direct;

        if (TryParseKV(norm, out var kv) && TryGetVal(kv, "date", out var dt) && TryFlexDate(dt, out var kvDate))
        {
            TryGetVal(kv, "description", out var d); TryGetVal(kv, "movementType", out var m); TryGetVal(kv, "paymentMethod", out var p);
            var a = TryGetVal(kv, "amount", out var ar) && TryFlexAmount(ar, out var pa) ? pa : 0;
            return new ExpenseEntryRequest(kvDate, d, a, m, p);
        }

        JsonNode? root; try { root = JsonNode.Parse(norm); } catch { return null; }
        if (root is JsonValue valNode && valNode.TryGetValue<string>(out var innerJson))
        {
            direct = TryDeserialize<ExpenseEntryRequest>(innerJson);
            if (direct is not null) return direct;
            try { root = JsonNode.Parse(innerJson); } catch { return null; }
        }
        if (root is not JsonObject payload) return null;
        if (!TryReadDate(payload, out var date)) return null;
        var desc = ReadStr(payload, "description"); var mt = ReadStr(payload, "movementType"); var pm = ReadStr(payload, "paymentMethod");
        TryReadAmount(payload, out var amount);
        return new ExpenseEntryRequest(date, desc, amount, mt, pm);
    }

    public static async Task<AuthLoginRequest?> ReadLoginAsync(HttpRequestData request, CancellationToken ct)
    {
        var body = await ReadRawBodyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(body)) return null;
        var login = TryDeserialize<AuthLoginRequest>(body);
        if (login is not null) return login;
        if (TryParseKV(body, out var kv) && TryGetVal(kv, "email", out var e) && TryGetVal(kv, "password", out var p))
            return new AuthLoginRequest(e, p);
        return null;
    }

    public static async Task<AuthRegisterRequest?> ReadRegisterAsync(HttpRequestData request, CancellationToken ct)
    {
        var body = await ReadRawBodyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(body)) return null;
        var reg = TryDeserialize<AuthRegisterRequest>(body);
        if (reg is not null) return reg;
        if (TryParseKV(body, out var kv) && TryGetVal(kv, "name", out var n) && TryGetVal(kv, "email", out var e) && TryGetVal(kv, "password", out var p))
            return new AuthRegisterRequest(n, e, p);
        return null;
    }

    // ── Private parsing helpers ──

    private static async Task<string> ReadRawBodyAsync(HttpRequestData request, CancellationToken ct)
    {
        try { if (request.Body.CanSeek) request.Body.Position = 0; using var r = new StreamReader(request.Body, Encoding.UTF8, true, 1024, true); return await r.ReadToEndAsync(ct); }
        catch { return ""; }
    }

    private static string NormalizeRawPayload(string raw)
    {
        var t = raw.Trim(); if (string.IsNullOrWhiteSpace(t)) return "";
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        { try { var n = JsonNode.Parse(t); if (n is JsonValue v && v.TryGetValue<string>(out var i) && !string.IsNullOrWhiteSpace(i)) return i.Trim(); } catch { } }
        return t;
    }

    private static T? TryDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch { return default; }
    }

    private static bool TryParseKV(string raw, out Dictionary<string, string> vals)
    {
        vals = new(StringComparer.OrdinalIgnoreCase);
        var norm = NormalizeRawPayload(raw).Trim(); if (!norm.Contains('=')) return false;
        foreach (var part in norm.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        { var p = part.IndexOf('='); if (p <= 0) continue; var k = Uri.UnescapeDataString(part[..p]).Trim(); var v = Uri.UnescapeDataString(part[(p + 1)..]).Trim(); if (!string.IsNullOrWhiteSpace(k)) vals[k] = v; }
        return vals.Count > 0;
    }

    private static bool TryGetVal(Dictionary<string, string> vals, string key, out string value)
    { if (vals.TryGetValue(key, out var f) && !string.IsNullOrWhiteSpace(f)) { value = f; return true; } value = ""; return false; }

    private static bool TryFlexDate(string? val, out DateOnly date)
    {
        if (DateOnly.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateOnly.TryParseExact(val, "dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"), DateTimeStyles.None, out date)) return true;
        if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) { date = DateOnly.FromDateTime(dt); return true; }
        return DateOnly.TryParse(val, out date);
    }

    private static bool TryFlexAmount(string? raw, out decimal amount)
    {
        var n = (raw ?? "").Trim(); if (string.IsNullOrWhiteSpace(n)) { amount = 0; return false; }
        if (n.Contains('.') && !n.Contains(',') && IsThousandsGrouping(n)) { if (decimal.TryParse(n.Replace(".", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) return true; }
        if (decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) return true;
        if (decimal.TryParse(n, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount)) return true;
        var digits = new string(n.Where(char.IsDigit).ToArray());
        return !string.IsNullOrWhiteSpace(digits) && decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool IsThousandsGrouping(string v) { var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries); if (parts.Length <= 1) return false; if (parts[0].Length is < 1 or > 3 || !parts[0].All(char.IsDigit)) return false; for (var i = 1; i < parts.Length; i++) if (parts[i].Length != 3 || !parts[i].All(char.IsDigit)) return false; return true; }

    private static bool TryReadDate(JsonObject p, out DateOnly date)
    {
        var node = FindProp(p, "date"); if (node is null) { date = default; return false; }
        if (node is JsonValue vn) { if (vn.TryGetValue<DateOnly>(out date)) return true; if (vn.TryGetValue<DateTime>(out var dt)) { date = DateOnly.FromDateTime(dt); return true; } if (vn.TryGetValue<string>(out var rd)) return TryFlexDate(rd, out date); }
        date = default; return false;
    }

    private static bool TryReadAmount(JsonObject p, out decimal amount)
    {
        var node = FindProp(p, "amount"); if (node is JsonValue vn) { if (vn.TryGetValue<decimal>(out amount)) return true; if (vn.TryGetValue<double>(out var d)) { amount = (decimal)d; return true; } if (vn.TryGetValue<string>(out var s)) return TryFlexAmount(s, out amount); }
        amount = 0; return false;
    }

    private static string ReadStr(JsonObject p, string name) { var n = FindProp(p, name); return n is JsonValue vn && vn.TryGetValue<string>(out var t) ? t?.Trim() ?? "" : ""; }

    private static JsonNode? FindProp(JsonObject p, string name) { foreach (var i in p) if (string.Equals(i.Key, name, StringComparison.OrdinalIgnoreCase)) return i.Value; return null; }
}
