using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Controleo.Api.Models;
using Controleo.Api.Services.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Controleo.Api.Functions;

public sealed class AuthApiFunctions
{
    private readonly IUserAuthService _userAuthService;
    private readonly ILogger<AuthApiFunctions> _logger;

    public AuthApiFunctions(IUserAuthService userAuthService, ILogger<AuthApiFunctions> logger)
    {
        _userAuthService = userAuthService;
        _logger = logger;
    }

    [Function("Register")]
    public async Task<HttpResponseData> RegisterAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await ReadAuthRegisterAsync(request, cancellationToken, _logger);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var validation = ValidateRegister(payload);
        if (validation.Count > 0)
        {
            return await ValidationProblemAsync(request, validation, cancellationToken);
        }

        var result = await _userAuthService.RegisterAsync(payload, cancellationToken);
        if (!result.IsSuccess || result.Session is null)
        {
            var status = string.Equals(result.ErrorMessage, "Este correo ya está registrado.", StringComparison.Ordinal)
                ? HttpStatusCode.Conflict
                : HttpStatusCode.BadRequest;

            return await JsonAsync(request, status, new OperationResult(false, result.ErrorMessage), cancellationToken);
        }

        return await JsonAsync(request, HttpStatusCode.Created, result.Session, cancellationToken);
    }

    [Function("Login")]
    public async Task<HttpResponseData> LoginAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await ReadAuthLoginAsync(request, cancellationToken, _logger);
        if (payload is null)
        {
            return await JsonAsync(request, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), cancellationToken);
        }

        var validation = ValidateLogin(payload);
        if (validation.Count > 0)
        {
            return await ValidationProblemAsync(request, validation, cancellationToken);
        }

        var result = await _userAuthService.LoginAsync(payload, cancellationToken);
        if (!result.IsSuccess || result.Session is null)
        {
            return await JsonAsync(request, HttpStatusCode.Unauthorized, new OperationResult(false, result.ErrorMessage), cancellationToken);
        }

        return await JsonAsync(request, HttpStatusCode.OK, result.Session, cancellationToken);
    }

    private static async Task<AuthLoginRequest?> ReadAuthLoginAsync(HttpRequestData request, CancellationToken cancellationToken, ILogger logger)
    {
        var body = await ReadRawBodyAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var login = TryDeserialize<AuthLoginRequest>(body);
        if (login is not null)
        {
            return login;
        }

        if (TryParseKeyValueBody(body, out var values)
            && TryGetValue(values, "email", out var email)
            && TryGetValue(values, "password", out var password))
        {
            return new AuthLoginRequest(email, password);
        }

        logger.LogWarning("Unable to parse login payload. ContentType={ContentType}", request.Headers.TryGetValues("Content-Type", out var contentTypes) ? string.Join(",", contentTypes) : "(none)");
        return null;
    }

    private static async Task<AuthRegisterRequest?> ReadAuthRegisterAsync(HttpRequestData request, CancellationToken cancellationToken, ILogger logger)
    {
        var body = await ReadRawBodyAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var register = TryDeserialize<AuthRegisterRequest>(body);
        if (register is not null)
        {
            return register;
        }

        if (TryParseKeyValueBody(body, out var values)
            && TryGetValue(values, "name", out var name)
            && TryGetValue(values, "email", out var email)
            && TryGetValue(values, "password", out var password))
        {
            return new AuthRegisterRequest(name, email, password);
        }

        logger.LogWarning("Unable to parse register payload. ContentType={ContentType}", request.Headers.TryGetValues("Content-Type", out var contentTypes) ? string.Join(",", contentTypes) : "(none)");
        return null;
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static T? TryDeserialize<T>(string raw)
    {
        var normalized = NormalizeRawPayload(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(normalized, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch
        {
            return default;
        }
    }

    private static bool TryParseKeyValueBody(string raw, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = NormalizeRawPayload(raw).Trim();
        if (!normalized.Contains('='))
        {
            return false;
        }

        var parts = normalized.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var pivot = part.IndexOf('=');
            if (pivot <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..pivot]).Trim();
            var value = Uri.UnescapeDataString(part[(pivot + 1)..]).Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values.Count > 0;
    }

    private static string NormalizeRawPayload(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                var jsonNode = JsonNode.Parse(trimmed);
                if (jsonNode is JsonValue valueNode && valueNode.TryGetValue<string>(out var inner) && !string.IsNullOrWhiteSpace(inner))
                {
                    return inner.Trim();
                }
            }
            catch
            {
            }
        }

        return trimmed;
    }

    private static bool TryGetValue(Dictionary<string, string> values, string key, out string value)
    {
        if (values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static async Task<HttpResponseData> JsonAsync(HttpRequestData request, HttpStatusCode statusCode, object payload, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(payload, cancellationToken: cancellationToken);
        response.StatusCode = statusCode;
        AddCorsHeaders(response);
        return response;
    }

    private static void AddCorsHeaders(HttpResponseData response)
    {
        if (!response.Headers.TryGetValues("Access-Control-Allow-Origin", out _))
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
        }

        if (!response.Headers.TryGetValues("Access-Control-Allow-Headers", out _))
        {
            response.Headers.Add("Access-Control-Allow-Headers", "authorization, content-type, x-requested-with");
        }

        if (!response.Headers.TryGetValues("Access-Control-Allow-Methods", out _))
        {
            response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
        }
    }

    private static async Task<HttpResponseData> ValidationProblemAsync(HttpRequestData request, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        var payload = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = (int)HttpStatusCode.BadRequest,
            errors
        };

        return await JsonAsync(request, HttpStatusCode.BadRequest, payload, cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateRegister(AuthRegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["El nombre es requerido."];
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            errors["email"] = ["Correo inválido."];
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Trim().Length < 8)
        {
            errors["password"] = ["La contraseña debe tener al menos 8 caracteres."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateLogin(AuthLoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["El correo es requerido."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["La contraseña es requerida."];
        }

        return errors;
    }
}