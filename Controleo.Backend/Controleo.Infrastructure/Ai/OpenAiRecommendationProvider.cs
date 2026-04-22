using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Controleo.Application.Options;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Extensions.Options;

namespace Controleo.Infrastructure.Ai;

public sealed class OpenAiRecommendationProvider(IOptions<LlmProviderOptions> options) : IAiRecommendationProvider
{
    private static readonly HttpClient SharedClient = new();
    private readonly LlmProviderOptions _options = Normalize(options.Value);

    public async Task<AiRecommendationResult> GenerateRecommendationsAsync(
        string compactSummary,
        IReadOnlyList<string> fallbackRecommendations,
        CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return new AiRecommendationResult(false, "Heuristica local", "Media", fallbackRecommendations, "Proveedor IA deshabilitado.");
        }

        var apiKey = ResolveApiKey(_options.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiRecommendationResult(false, "Heuristica local", "Media", fallbackRecommendations, "No se encontro API key del proveedor IA.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildCompletionsEndpoint(_options.ApiBaseUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                model = _options.Model,
                temperature = (double)_options.Temperature,
                max_tokens = _options.MaxTokens,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Eres un asesor financiero personal de bajo costo. Responde solo JSON valido con llaves priority y recommendations. priority debe ser Alta, Media o Baja. recommendations debe tener de 1 a 3 strings, cada string con accion concreta, meta numerica y horizonte (dias o semanas), usando datos del contexto. No incluyas texto fuera del JSON."
                    },
                    new
                    {
                        role = "user",
                        content = compactSummary
                    }
                }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            var response = await SharedClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new AiRecommendationResult(
                    false,
                    "Heuristica local",
                    "Media",
                    fallbackRecommendations,
                    $"Proveedor IA retorno {(int)response.StatusCode}.");
            }

            var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var parsed = TryParseAiResponse(raw);
            if (parsed.Recommendations.Count == 0)
            {
                return new AiRecommendationResult(false, "Heuristica local", "Media", fallbackRecommendations, "Respuesta IA sin recomendaciones accionables.");
            }

            return new AiRecommendationResult(
                true,
                "IA personalizada",
                parsed.Priority,
                parsed.Recommendations,
                null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AiRecommendationResult(false, "Heuristica local", "Media", fallbackRecommendations, "Timeout consultando IA.");
        }
        catch (Exception ex)
        {
            return new AiRecommendationResult(false, "Heuristica local", "Media", fallbackRecommendations, ex.Message);
        }
    }

    private static (string Priority, IReadOnlyList<string> Recommendations) TryParseAiResponse(string raw)
    {
        try
        {
            using var root = JsonDocument.Parse(raw);
            var content = root.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return ("Media", []);
            }

            using var messageDoc = JsonDocument.Parse(content);
            var priority = "Media";
            if (messageDoc.RootElement.TryGetProperty("priority", out var priorityNode)
                && priorityNode.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(priorityNode.GetString()))
            {
                priority = NormalizePriority(priorityNode.GetString()!);
            }

            if (messageDoc.RootElement.TryGetProperty("recommendations", out var recommendationsNode)
                && recommendationsNode.ValueKind == JsonValueKind.Array)
            {
                var recommendations = recommendationsNode
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToArray();

                return (priority, recommendations);
            }
        }
        catch
        {
        }

        return ("Media", []);
    }

    private static string NormalizePriority(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "alta" => "Alta",
            "media" => "Media",
            "baja" => "Baja",
            _ => "Media"
        };
    }

    private static string BuildCompletionsEndpoint(string apiBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.openai.com/v1/" : apiBaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return $"{baseUrl}chat/completions";
    }

    private static LlmProviderOptions Normalize(LlmProviderOptions options)
    {
        return new LlmProviderOptions
        {
            Enabled = options.Enabled,
            ApiBaseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "https://api.openai.com/v1/" : options.ApiBaseUrl.Trim(),
            ApiKey = options.ApiKey ?? string.Empty,
            Model = string.IsNullOrWhiteSpace(options.Model) ? "gpt-4.1-nano" : options.Model.Trim(),
            MaxTokens = options.MaxTokens <= 0 ? 260 : options.MaxTokens,
            Temperature = options.Temperature <= 0 ? 0.2m : options.Temperature,
            RequestTimeoutSeconds = options.RequestTimeoutSeconds <= 0 ? 20 : options.RequestTimeoutSeconds
        };
    }

    private static string ResolveApiKey(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && !configured.StartsWith("TU_", StringComparison.OrdinalIgnoreCase)
            && !configured.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase))
        {
            return configured.Trim();
        }

        var envValue = Environment.GetEnvironmentVariable("LlmProvider__ApiKey");
        if (!string.IsNullOrWhiteSpace(envValue)
            && !envValue.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase))
        {
            return envValue.Trim();
        }

        return string.Empty;
    }
}
