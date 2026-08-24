using System.Collections.Concurrent;
using Wasta.Application.Features.Notifications;

namespace Wasta.WebApi;

/// <summary>
/// An in-memory mail catcher for local development, in the spirit of MailHog.
///
/// Sign-in requires a confirmed address, and confirming means following a link
/// that only exists in an email. On a laptop with no mail provider that link
/// goes nowhere, which makes the sign-up flow impossible to test end to end.
///
/// This captures what would have been sent so a developer can read it. It is
/// registered ONLY in the Development environment - see Program.cs, which also
/// refuses to expose the endpoint anywhere else. It deliberately does not mark
/// anything verified: the real token, the real confirm endpoint and the real
/// expiry all still apply. It only delivers the letter.
/// </summary>
public sealed class DevMailbox(ILogger<DevMailbox> logger) : INotificationSender
{
    private const int Capacity = 50;

    private readonly ConcurrentQueue<CapturedMessage> _messages = new();

    public bool IsRealSender => false;

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        _messages.Enqueue(new CapturedMessage(
            message.Recipient, message.Subject, message.Body, DateTimeOffset.UtcNow, TokenIn(message.Body)));

        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
            // Bounded: this is a debugging aid, not storage.
        }

        logger.LogInformation("Dev mailbox captured a message to {Recipient}.", message.Recipient);
        return Task.CompletedTask;
    }

    public IReadOnlyList<CapturedMessage> Messages => _messages.Reverse().ToArray();

    /// <summary>
    /// Pulls the token out of the link so the client can offer a one-click
    /// confirm. The token is a bearer credential; this is why the whole class
    /// is Development-only.
    /// </summary>
    private static string? TokenIn(string body)
    {
        const string marker = "token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var token = body[(start + marker.Length)..];
        var end = token.IndexOfAny([' ', '\n', '\r', '"', '<']);

        return end < 0 ? token : token[..end];
    }

    public sealed record CapturedMessage(
        string Recipient, string Subject, string Body, DateTimeOffset CapturedAt, string? Token);
}
