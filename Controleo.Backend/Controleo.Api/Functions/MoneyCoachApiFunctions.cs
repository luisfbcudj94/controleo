using System.Net;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Auth;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace Controleo.Api.Functions;
public sealed class MoneyCoachApiFunctions
{
    private readonly IMoneyCoachService _coachService;
    private readonly IAccessTokenValidator _tokenValidator;
    private readonly LocalAuthOptions _authOptions;
    private readonly IHostEnvironment _env;

    public MoneyCoachApiFunctions(IMoneyCoachService coachService, IAccessTokenValidator tokenValidator, IOptions<LocalAuthOptions> authOptions, IHostEnvironment env)
    {
        _coachService = coachService;
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
            return (null, await FunctionHelpers.JsonAsync(req, HttpStatusCode.Forbidden, new { message = "Coach IA está disponible solo para usuarios premium." }, ct));
        return (user, null);
    }

    [Function("SendCoachMessage")]
    public async Task<HttpResponseData> SendMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "coach/message")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var payload = await FunctionHelpers.ReadBodyAsync<ChatMessageRequest>(req, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Content))
            return await FunctionHelpers.JsonAsync(req, HttpStatusCode.BadRequest, new { message = "El mensaje es requerido." }, ct);
        var result = await _coachService.SendMessageAsync(user!.UserId, payload.Content, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result, ct);
    }

    [Function("GetCoachHistory")]
    public async Task<HttpResponseData> GetHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "coach/history")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var query = FunctionHelpers.ParseQuery(req);
        var limit = int.TryParse(query.GetValueOrDefault("limit"), out var l) ? l : 20;
        var result = await _coachService.GetHistoryAsync(user!.UserId, limit, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result, ct);
    }

    [Function("ClearCoachHistory")]
    public async Task<HttpResponseData> ClearHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "coach/history")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var result = await _coachService.ClearHistoryAsync(user!.UserId, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result, ct);
    }

    [Function("GetCoachSuggestions")]
    public async Task<HttpResponseData> GetSuggestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "coach/suggestions")] HttpRequestData req, CancellationToken ct)
    {
        var (user, err) = await AuthPremiumAsync(req, ct);
        if (err is not null) return err;
        var result = await _coachService.GetSuggestionsAsync(user!.UserId, ct);
        return await FunctionHelpers.JsonAsync(req, HttpStatusCode.OK, result, ct);
    }
}
