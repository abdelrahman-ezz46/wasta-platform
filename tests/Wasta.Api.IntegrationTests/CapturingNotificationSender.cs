using System.Collections.Concurrent;
using Wasta.Application.Features.Notifications;

namespace Wasta.Api.IntegrationTests;

/// <summary>
/// Records what would have been sent, so a test can read the link out of a
/// verification or reset email. Succeeds like the real sender would, so the
/// dispatcher tests still see delivery working.
/// </summary>
public sealed class CapturingNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<OutboundMessage> _sent = new();

    public bool IsRealSender => false;

    /// <summary>
    /// Simulates a mail provider outage. Set for a single test, in a collection
    /// that runs sequentially, and reset in a finally.
    /// </summary>
    public bool ThrowOnSend { get; set; }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("Simulated mail provider outage.");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<OutboundMessage> Sent => _sent.ToArray();

    public OutboundMessage? LastTo(string recipient) =>
        _sent.Where(m => m.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase)).LastOrDefault();

    /// <summary>Pulls the token out of the emailed link, which is where a real user gets it.</summary>
    public static string? TokenFrom(OutboundMessage? message)
    {
        if (message is null)
        {
            return null;
        }

        var marker = "token=";
        var index = message.Body.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var token = message.Body[(index + marker.Length)..];
        var end = token.IndexOfAny([' ', '\n', '\r']);

        return end < 0 ? token : token[..end];
    }
}
