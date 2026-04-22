using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Controleo.Mobile.Core.Interfaces;
using Microsoft.Maui.Storage;

namespace Controleo.Mobile.Core.Services;

public sealed class AuthService(HttpClient httpClient) : IAuthService
{
    private const string SessionKey = "controleo_auth_session";

    private AuthSession? _session;
    public event Action? SessionCleared;

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        EnsureSessionLoaded();
        return Task.FromResult(_session is not null && !IsSessionExpired(_session));
    }

    public async Task<(bool IsSuccess, string ErrorMessage)> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Correo y contraseña son obligatorios.");
        }

        try
        {
            using var request = CreateJsonRequest(
                HttpMethod.Post,
                "api/auth/login",
                new Dictionary<string, string>
                {
                    ["email"] = email.Trim(),
                    ["password"] = password
                });

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response, cancellationToken);
                return (false, error);
            }

            var session = await response.Content.ReadFromJsonAsync<AuthSession>(cancellationToken: cancellationToken);
            if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return (false, "No fue posible leer la sesión del servidor.");
            }

            SetSession(session);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool IsSuccess, string ErrorMessage)> RegisterAsync(string name, string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "El nombre es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Correo y contraseña son obligatorios.");
        }

        if (password.Trim().Length < 8)
        {
            return (false, "La contraseña debe tener al menos 8 caracteres.");
        }

        try
        {
            using var request = CreateJsonRequest(
                HttpMethod.Post,
                "api/auth/register",
                new Dictionary<string, string>
                {
                    ["name"] = name.Trim(),
                    ["email"] = email.Trim(),
                    ["password"] = password
                });

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response, cancellationToken);
                return (false, error);
            }

            var session = await response.Content.ReadFromJsonAsync<AuthSession>(cancellationToken: cancellationToken);
            if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
            {
                return (false, "No fue posible leer la sesión del servidor.");
            }

            SetSession(session);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        EnsureSessionLoaded();
        if (_session is null || IsSessionExpired(_session))
        {
            await SignOutAsync(cancellationToken);
            return null;
        }

        return _session.AccessToken;
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        Preferences.Default.Remove(SessionKey);
        SessionCleared?.Invoke();
        return Task.CompletedTask;
    }

    public string CurrentUserId
    {
        get
        {
            EnsureSessionLoaded();
            return _session?.User?.UserId ?? string.Empty;
        }
    }

    public string CurrentUserName
    {
        get
        {
            EnsureSessionLoaded();
            return _session?.User?.Name ?? _session?.User?.Email ?? "Controleo";
        }
    }

    public string CurrentUserEmail
    {
        get
        {
            EnsureSessionLoaded();
            return _session?.User?.Email ?? "sin-correo";
        }
    }

    public decimal? CurrentMonthlyIncome
    {
        get
        {
            EnsureSessionLoaded();
            return _session?.User?.MonthlyIncome;
        }
    }

    public bool IsCurrentUserPremium
    {
        get
        {
            EnsureSessionLoaded();
            return _session?.User?.IsPremium ?? false;
        }
    }

    public void UpdateCurrentMonthlyIncome(decimal? monthlyIncome)
    {
        EnsureSessionLoaded();
        if (_session is null || _session.User is null)
        {
            return;
        }

        _session = _session with
        {
            User = _session.User with
            {
                MonthlyIncome = monthlyIncome
            }
        };

        var raw = System.Text.Json.JsonSerializer.Serialize(_session);
        Preferences.Default.Set(SessionKey, raw);
    }

    public string ApiBaseUrl => httpClient.BaseAddress?.ToString() ?? "(sin base URL)";

    private void EnsureSessionLoaded()
    {
        if (_session is not null)
        {
            return;
        }

        var raw = Preferences.Default.Get(SessionKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            _session = System.Text.Json.JsonSerializer.Deserialize<AuthSession>(raw);
        }
        catch
        {
            _session = null;
            Preferences.Default.Remove(SessionKey);
        }
    }

    private void SetSession(AuthSession session)
    {
        _session = session;
        var raw = System.Text.Json.JsonSerializer.Serialize(session);
        Preferences.Default.Set(SessionKey, raw);
    }

    private static bool IsSessionExpired(AuthSession session)
    {
        if (string.IsNullOrWhiteSpace(session.ExpiresAt) || !DateTimeOffset.TryParse(session.ExpiresAt, out var expiresAt))
        {
            return true;
        }

        return DateTimeOffset.UtcNow >= expiresAt;
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var endpoint = response.RequestMessage?.RequestUri?.ToString() ?? "(endpoint desconocido)";
        var status = (int)response.StatusCode;

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
            if (payload is not null && !string.IsNullOrWhiteSpace(payload.Message))
            {
                return $"{payload.Message} · [{status}] {endpoint}";
            }
        }
        catch
        {
        }

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return $"{raw} · [{status}] {endpoint}";
            }
        }
        catch
        {
        }

        return $"Error de autenticación ({status}) · {endpoint}";
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string relativeUrl, Dictionary<string, string> payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record OperationResult(bool IsSuccess, string Message);
    private sealed record AuthUser(string UserId, string Name, string Email, bool IsPremium = false, decimal? MonthlyIncome = null);
    private sealed record AuthSession(string AccessToken, string ExpiresAt, AuthUser User);
}
