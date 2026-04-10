using System.Net;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Functions;

public sealed class AdminApiFunctions
{
    private readonly IAdminUserService _adminUserService;
    private readonly IUserAuthService _userAuthService;
    private readonly IAccessTokenValidator _tokenValidator;
    private readonly LocalAuthOptions _authOptions;
    private readonly IHostEnvironment _env;

    public AdminApiFunctions(
        IAdminUserService adminUserService,
        IUserAuthService userAuthService,
        IAccessTokenValidator tokenValidator,
        IOptions<LocalAuthOptions> authOptions,
        IHostEnvironment env)
    {
        _adminUserService = adminUserService;
        _userAuthService = userAuthService;
        _tokenValidator = tokenValidator;
        _authOptions = authOptions.Value;
        _env = env;
    }

    private Task<(ApiUserContext? User, HttpResponseData? Response)> AuthAsync(HttpRequestData req, CancellationToken ct)
        => FunctionHelpers.AuthorizeAsync(req, _tokenValidator, _authOptions, _env.IsDevelopment(), ct);

    [Function("GetAdminUsersOptions")]
    public HttpResponseData GetUsersOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "management/users")] HttpRequestData req)
        => FunctionHelpers.NoContent(req);

    [Function("UpdateAdminUserOptions")]
    public HttpResponseData UpdateUserOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "management/users/{userId}")] HttpRequestData req,
        string userId)
    {
        _ = userId;
        return FunctionHelpers.NoContent(req);
    }

    [Function("ImpersonateUserOptions")]
    public HttpResponseData ImpersonateUserOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "management/users/{userId}/impersonate")] HttpRequestData req,
        string userId)
    {
        _ = userId;
        return FunctionHelpers.NoContent(req);
    }

    [Function("EndImpersonationOptions")]
    public HttpResponseData EndImpersonationOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "management/impersonation/end")] HttpRequestData req)
        => FunctionHelpers.NoContent(req);

    [Function("GetAdminUsers")]
    public async Task<HttpResponseData> GetUsersAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/users")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null)
            return err;

        if (user is null || !user.IsAdmin)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new OperationResult(false, "Solo administradores."), ct);

        var q = FunctionHelpers.ParseQuery(req);
        var search = q.TryGetValue("search", out var searchValue)
            ? searchValue
            : q.TryGetValue("searchTerm", out var searchTermValue) ? searchTermValue : null;

        var pageNumber = q.TryGetValue("pageNumber", out var pageNumberRaw) && int.TryParse(pageNumberRaw, out var parsedPageNumber)
            ? Math.Max(parsedPageNumber, 1)
            : 1;
        var pageSize = q.TryGetValue("pageSize", out var pageSizeRaw) && int.TryParse(pageSizeRaw, out var parsedPageSize)
            ? FunctionHelpers.NormalizePageSize(parsedPageSize)
            : 5;

        var users = await _adminUserService.GetUsersPageAsync(search, pageNumber, pageSize, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, users, ct);
    }

    [Function("UpdateAdminUser")]
    public async Task<HttpResponseData> UpdateUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/users/{userId}")] HttpRequestData req,
        string userId,
        CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null)
            return err;

        if (user is null || !user.IsAdmin)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new OperationResult(false, "Solo administradores."), ct);

        var payload = await FunctionHelpers.ReadBodyAsync<AdminUserUpdateRequest>(req, ct);
        if (payload is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);

        var result = await _adminUserService.UpdateUserAccessAsync(user.UserId, userId, payload, ct);
        var status = result.IsSuccess
            ? HttpStatusCode.OK
            : result.Message.Contains("no encontrado", StringComparison.OrdinalIgnoreCase)
                ? HttpStatusCode.NotFound
                : result.Message.Contains("no puedes", StringComparison.OrdinalIgnoreCase) ||
                  result.Message.Contains("super-admin", StringComparison.OrdinalIgnoreCase)
                    ? HttpStatusCode.Forbidden
                : HttpStatusCode.BadRequest;

        return await FunctionHelpers.JsonAsync(req, status, result, ct);
    }

    [Function("DeleteAdminUser")]
    public async Task<HttpResponseData> DeleteUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/users/{userId}")] HttpRequestData req,
        string userId,
        CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null)
            return err;

        if (user is null || !user.IsAdmin)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new OperationResult(false, "Solo administradores."), ct);

        var result = await _adminUserService.DeleteUserAndDataAsync(user.UserId, userId, ct);
        var status = result.IsSuccess
            ? HttpStatusCode.OK
            : result.Message.Contains("no encontrado", StringComparison.OrdinalIgnoreCase)
                ? HttpStatusCode.NotFound
                : result.Message.Contains("no puedes", StringComparison.OrdinalIgnoreCase) ||
                  result.Message.Contains("super-admin", StringComparison.OrdinalIgnoreCase)
                    ? HttpStatusCode.Forbidden
                : HttpStatusCode.BadRequest;

        return await FunctionHelpers.JsonAsync(req, status, result, ct);
    }

    [Function("ImpersonateAdminUser")]
    public async Task<HttpResponseData> ImpersonateUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/users/{userId}/impersonate")] HttpRequestData req,
        string userId,
        CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null)
            return err;

        if (user is null || !user.IsAdmin)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new OperationResult(false, "Solo administradores."), ct);

        var impersonation = await _userAuthService.ImpersonateAsync(user.UserId, userId, ct);

        var auditOnFailure = await _adminUserService.LogImpersonationEventAsync(user.UserId, userId, "impersonation_start", impersonation.IsSuccess, impersonation.ErrorMessage, ct);

        if (!impersonation.IsSuccess || impersonation.Session is null)
        {
            var status = impersonation.ErrorMessage.Contains("no encontrado", StringComparison.OrdinalIgnoreCase)
                ? HttpStatusCode.NotFound
                : impersonation.ErrorMessage.Contains("solo administradores", StringComparison.OrdinalIgnoreCase) ||
                  impersonation.ErrorMessage.Contains("no se puede", StringComparison.OrdinalIgnoreCase)
                    ? HttpStatusCode.Forbidden
                    : HttpStatusCode.BadRequest;

            var payload = new OperationResult(false, string.IsNullOrWhiteSpace(impersonation.ErrorMessage)
                ? "No fue posible iniciar suplantación."
                : impersonation.ErrorMessage);

            return await FunctionHelpers.JsonAsync(req, status, payload, ct);
        }

        if (!auditOnFailure.IsSuccess)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.InternalServerError, auditOnFailure, ct);

        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, impersonation.Session, ct);
    }

    [Function("EndImpersonation")]
    public async Task<HttpResponseData> EndImpersonationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/impersonation/end")] HttpRequestData req,
        CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null)
            return err;

        if (user is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Unauthorized, new OperationResult(false, "Sesión inválida."), ct);

        if (!user.IsImpersonating || string.IsNullOrWhiteSpace(user.ActorUserId))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "No hay suplantación activa."), ct);

        var audit = await _adminUserService.LogImpersonationEventAsync(
            user.ActorUserId,
            user.UserId,
            "impersonation_end",
            true,
            "Cierre de suplantación desde cliente web.",
            ct);

        var status = audit.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
        return await FunctionHelpers.JsonAsync(req, status, audit, ct);
    }
}