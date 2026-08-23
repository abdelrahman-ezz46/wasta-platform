using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wasta.Ai;

/// <summary>Google's native generateContent REST endpoint.</summary>
public class GeminiProvider : IAiProvider
{
    public string Name => "gemini";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options, ILogger<GeminiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    private AiProviderOptions? Config => _options.Providers.GetValueOrDefault(Name);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Config?.ApiKey)
        && !string.IsNullOrWhiteSpace(Config?.BaseUrl)
        && !string.IsNullOrWhiteSpace(Config?.Model);

    public async Task<string> CompleteAsync(string systemPrompt, IReadOnlyList<AiChatTurn> turns, AiCallOptions? callOptions, CancellationToken ct)
    {
        var config = Config ?? throw new AiUnavailableException($"Provider '{Name}' is not configured.");

        var client = _httpClientFactory.CreateClient($"ai-{Name}");
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        var model = AiModelResolver.ResolveModel(callOptions?.Model, config.Model);
        var url = $"{config.BaseUrl.TrimEnd('/')}/{model}:generateContent?key={Uri.EscapeDataString(config.ApiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = JsonContent.Create(new GeminiRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = [new GeminiPart { Text = systemPrompt }],
            },
            Contents = turns
                .Select(t => new GeminiContent
                {
                    // Gemini calls the assistant role "model", not "assistant".
                    Role = t.Role == "assistant" ? "model" : "user",
                    Parts = [new GeminiPart { Text = t.Content }],
                })
                .ToList(),
            GenerationConfig = new GeminiGenerationConfig
            {
                MaxOutputTokens = callOptions?.MaxTokens ?? _options.MaxTokens,
                Temperature = callOptions?.Temperature ?? _options.Temperature,
            },
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiTransientFailureException($"{Name} request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiTransientFailureException($"{Name} request failed.", ex);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            throw new AiTransientFailureException($"{Name} returned {(int)response.StatusCode}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("{Provider} returned non-retryable status {Status}: {Body}", Name, response.StatusCode, body);
            throw new InvalidOperationException($"{Name} returned {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{Name} returned an empty completion.");
        }

        return text;
    }

    private sealed class GeminiRequest
    {
        [JsonPropertyName("system_instruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = new();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }
}
