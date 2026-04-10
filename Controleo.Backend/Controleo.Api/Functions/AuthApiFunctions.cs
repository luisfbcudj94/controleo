using System.Net;
using Controleo.Application.Validation;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Functions;

public sealed class AuthApiFunctions
{
    private readonly IUserAuthService _authService;
    private readonly ILogger<AuthApiFunctions> _logger;

    public AuthApiFunctions(IUserAuthService authService, ILogger<AuthApiFunctions> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [Function("Register")]
    public async Task<HttpResponseData> RegisterAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData req, CancellationToken ct)
    {
        var payload = await FunctionHelpers.ReadRegisterAsync(req, ct);
        if (payload is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);

        var errors = AuthValidator.ValidateRegister(payload);
        if (errors.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, errors, ct);

        var result = await _authService.RegisterAsync(payload.Name, payload.Email, payload.Password, ct);
        if (!result.IsSuccess || result.Session is null)
        {
            var status = string.Equals(result.ErrorMessage, "Este correo ya está registrado.", StringComparison.Ordinal)
                ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest;
            return await FunctionHelpers.JsonAsync(req, status, new OperationResult(false, result.ErrorMessage), ct);
        }

        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.Created, result.Session, ct);
    }

    [Function("Login")]
    public async Task<HttpResponseData> LoginAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req, CancellationToken ct)
    {
        var payload = await FunctionHelpers.ReadLoginAsync(req, ct);
        if (payload is null)
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new OperationResult(false, "Request inválido."), ct);

        var errors = AuthValidator.ValidateLogin(payload);
        if (errors.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, errors, ct);

        var result = await _authService.LoginAsync(payload.Email, payload.Password, ct);
        if (!result.IsSuccess || result.Session is null)
        {
            var status = string.Equals(result.ErrorMessage, "Cuenta inhabilitada por el administrador.", StringComparison.Ordinal)
                ? HttpStatusCode.Forbidden
                : HttpStatusCode.Unauthorized;
            return await FunctionHelpers.JsonAsync(req, status, new OperationResult(false, result.ErrorMessage), ct);
        }

        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result.Session, ct);
    }
}
