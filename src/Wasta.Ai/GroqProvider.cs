using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Wasta.Ai;

/// <summary>Groq's OpenAI-compatible chat completions endpoint.</summary>
public class GroqProvider : IAiProvider
{
    public string Name => "groq";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<GroqProvider> _logger;

    public GroqProvider(IHttpClientFactory httpClientFactory, IOptions<AiOptions> options, ILogger<GroqProvider> logger)
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

        var messages = new List<OpenAiChatMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(turns.Select(t => new OpenAiChatMessage { Role = t.Role, Content = t.Content }));

        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(new OpenAiChatRequest
        {
            Model = AiModelResolver.ResolveModel(callOptions?.Model, config.Model),
            Messages = messages,
            MaxTokens = callOptions?.MaxTokens ?? _options.MaxTokens,
            Temperature = callOptions?.Temperature ?? _options.Temperature,
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

        var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"{Name} returned an empty completion.");
        }

        return content;
    }

    private sealed class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenAiChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class OpenAiChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiChatMessage? Message { get; set; }
    }
}
