using System.Net;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Application.Validation;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Auth;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace Controleo.Api.Functions;
public sealed class GoalApiFunctions
{
    private readonly ISavingsGoalService _goalService;
    private readonly IAccessTokenValidator _tokenValidator;
    private readonly LocalAuthOptions _authOptions;
    private readonly IHostEnvironment _env;

    public GoalApiFunctions(ISavingsGoalService goalService, IAccessTokenValidator tokenValidator, IOptions<LocalAuthOptions> authOptions, IHostEnvironment env)
    {
        _goalService = goalService;
        _tokenValidator = tokenValidator;
        _authOptions = authOptions.Value;
        _env = env;
    }

    private Task<(ApiUserContext? User, HttpResponseData? Response)> AuthAsync(HttpRequestData req, CancellationToken ct)
        => FunctionHelpers.AuthorizeAsync(req, _tokenValidator, _authOptions, _env.IsDevelopment(), ct);

    private async Task<(ApiUserContext? User, HttpResponseData? Error)> AuthPremiumAsync(HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(req, ct);
        if (err is not null) return (null, err);
        if (!user!.IsPremium)
            return (null, await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new { message = "Las metas de ahorro están disponibles solo para usuarios premium." }, ct));
        return (user, null);
    }

    [Function("GetGoals")]
    public async Task<HttpResponseData> GetGoals(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var data = await _goalService.GetAllGoalsAsync(user!.UserId, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, data, ct);
    }

    [Function("GetGoal")]
    public async Task<HttpResponseData> GetGoal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var data = await _goalService.GetGoalProgressAsync(user!.UserId, id, ct);
        if (data is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.NotFound, new { message = "Meta no encontrada." }, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, data, ct);
    }

    [Function("CreateGoal")]
    public async Task<HttpResponseData> CreateGoal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<GoalUpsertRequest>(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new { message = "Request inválido." }, ct);
        var ve = SavingsGoalValidator.Validate(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _goalService.CreateGoalAsync(user!.UserId, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("UpdateGoal")]
    public async Task<HttpResponseData> UpdateGoal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "goals/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<GoalUpsertRequest>(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new { message = "Request inválido." }, ct);
        var ve = SavingsGoalValidator.Validate(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _goalService.UpdateGoalAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteGoal")]
    public async Task<HttpResponseData> DeleteGoal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "goals/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var result = await _goalService.DeleteGoalAsync(user!.UserId, id, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("AddGoalContribution")]
    public async Task<HttpResponseData> AddContribution(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals/{id}/contributions")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<GoalContributionRequest>(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new { message = "Request inválido." }, ct);
        var ve = SavingsGoalValidator.ValidateContribution(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _goalService.AddContributionAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.Created : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("DeleteGoalContribution")]
    public async Task<HttpResponseData> DeleteContribution(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "goals/{id}/contributions/{contributionId}")] HttpRequestData req,
        string id, string contributionId, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var result = await _goalService.DeleteContributionAsync(user!.UserId, id, contributionId, ct);
        return await FunctionHelpers.JsonAsync(req, result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, ct);
    }

    [Function("SimulateGoal")]
    public async Task<HttpResponseData> Simulate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals/{id}/simulate")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<GoalSimulationRequest>(req, ct);
        if (payload is null) return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new { message = "Request inválido." }, ct);
        var ve = SavingsGoalValidator.ValidateSimulation(payload);
        if (ve.Count > 0) return await FunctionHelpers.ValidationProblemAsync(req, ve, ct);
        var result = await _goalService.SimulateScenarioAsync(user!.UserId, id, payload, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result, ct);
    }

    [Function("GetGoalAlerts")]
    public async Task<HttpResponseData> GetAlerts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals/alerts")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var data = await _goalService.GetGoalAlertsAsync(user!.UserId, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, data, ct);
    }
}
