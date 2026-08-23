using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasta.Ai;

namespace Wasta.CareerCoach.Tests;

/// <summary>
/// The two features have opposite needs from a model - the coach runs once
/// per assessment and must emit strict JSON, chat runs on every message and
/// only needs a few sentences - so each can name its own. These tests pin
/// the wiring: an override is honoured, and its absence falls back to the
/// provider default rather than sending an empty model and 400-ing.
/// </summary>
public class PerFeatureModelTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":"ok"}}],
                     "candidates":[{"content":{"parts":[{"text":"ok"}]}}]}
                    """, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static AiOptions OptionsWith(string providerDefaultModel) => new()
    {
        Chain = ["groq", "gemini"],
        Providers = new Dictionary<string, AiProviderOptions>
        {
            ["groq"] = new() { BaseUrl = "https://groq.test/v1/chat", Model = providerDefaultModel, ApiKey = "test-key" },
            ["gemini"] = new() { BaseUrl = "https://gemini.test/v1beta/models", Model = providerDefaultModel, ApiKey = "test-key" },
        },
    };

    [Fact]
    public async Task Groq_UsesPerCallModel_WhenOverridden()
    {
        var handler = new CapturingHandler();
        var provider = new GroqProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GroqProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], new AiCallOptions(Model: "override-model"), CancellationToken.None);

        Assert.Contains("\"model\":\"override-model\"", handler.RequestBody);
        Assert.DoesNotContain("default-model", handler.RequestBody);
    }

    [Fact]
    public async Task Groq_FallsBackToProviderModel_WhenOverrideIsNull()
    {
        var handler = new CapturingHandler();
        var provider = new GroqProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GroqProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], new AiCallOptions(MaxTokens: 100), CancellationToken.None);

        Assert.Contains("\"model\":\"default-model\"", handler.RequestBody);
    }

    [Fact]
    public async Task Groq_FallsBackToProviderModel_WhenCallOptionsAreNull()
    {
        var handler = new CapturingHandler();
        var provider = new GroqProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GroqProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], callOptions: null, CancellationToken.None);

        Assert.Contains("\"model\":\"default-model\"", handler.RequestBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Groq_FallsBackToProviderModel_WhenOverrideIsEmpty(string emptyOverride)
    {
        var handler = new CapturingHandler();
        var provider = new GroqProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GroqProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], new AiCallOptions(Model: emptyOverride), CancellationToken.None);

        // Configuration binds an absent value to "" rather than null, and the
        // README documents `"Model": ""` as meaning "use the provider default".
        // A plain ?? only falls through on null, so this sent an EMPTY model
        // name and Groq answered 404 - which reads exactly like a deprecated
        // model and cost a real debugging session to tell apart.
        Assert.Contains("\"model\":\"default-model\"", handler.RequestBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Gemini_FallsBackToProviderModel_WhenOverrideIsEmpty(string emptyOverride)
    {
        var handler = new CapturingHandler();
        var provider = new GeminiProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GeminiProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], new AiCallOptions(Model: emptyOverride), CancellationToken.None);

        Assert.Contains("default-model", handler.RequestUri!.ToString());
    }

    [Fact]
    public async Task Gemini_UsesPerCallModelInTheUrl_WhenOverridden()
    {
        // Gemini names the model in the request path rather than the body,
        // so the override has to reach a different place than Groq's.
        var handler = new CapturingHandler();
        var provider = new GeminiProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GeminiProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], new AiCallOptions(Model: "override-model"), CancellationToken.None);

        Assert.Contains("/override-model:generateContent", handler.RequestUri);
        Assert.DoesNotContain("default-model", handler.RequestUri);
    }

    [Fact]
    public async Task Gemini_FallsBackToProviderModel_WhenOverrideIsNull()
    {
        var handler = new CapturingHandler();
        var provider = new GeminiProvider(new SingleClientFactory(handler), Options.Create(OptionsWith("default-model")), NullLogger<GeminiProvider>.Instance);

        await provider.CompleteAsync("system", [new AiChatTurn("user", "hi")], callOptions: null, CancellationToken.None);

        Assert.Contains("/default-model:generateContent", handler.RequestUri);
    }

    [Fact]
    public void IsConfigured_StillRequiresProviderModel_SoAnOverrideAloneIsNotEnough()
    {
        // Guards the trap: if a feature override alone satisfied IsConfigured,
        // a host that set only feature models would look configured but send
        // an empty model on any other call path.
        var handler = new CapturingHandler();
        var provider = new GroqProvider(new SingleClientFactory(handler), Options.Create(OptionsWith(providerDefaultModel: "")), NullLogger<GroqProvider>.Instance);

        Assert.False(provider.IsConfigured);
    }
}
