using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasta.Application.Features.Notifications;

namespace Wasta.Infrastructure.Notifications;

public sealed class SesNotificationSenderOptions
{
    public const string SectionName = "Email";

    /// <summary>Off by default. On, the logging stand-in is replaced entirely.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// SES region. Not necessarily where the app runs: SES is unavailable in
    /// several regions, so this is configured separately rather than inferred.
    /// </summary>
    public string Region { get; set; } = "eu-west-1";

    /// <summary>
    /// The verified sender identity. Required when enabled - SES rejects
    /// anything else, and a blank value binds as "" rather than null, so it is
    /// checked explicitly at startup instead of falling through a null test.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Optional SES configuration set, for bounce and complaint tracking.</summary>
    public string ConfigurationSet { get; set; } = string.Empty;
}

/// <summary>
/// Sends through Amazon SES v2.
///
/// Credentials are never read from configuration. The client uses the default
/// AWS credential chain, so a deployment supplies them through an IAM role and
/// nothing long-lived is stored in this repo, an environment variable, or a
/// secrets file.
/// </summary>
public sealed class SesNotificationSender(
    IAmazonSimpleEmailServiceV2 ses,
    IOptions<SesNotificationSenderOptions> options,
    ILogger<SesNotificationSender> logger) : INotificationSender
{
    public bool IsRealSender => true;

    public async Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        var settings = options.Value;

        var request = new SendEmailRequest
        {
            FromEmailAddress = settings.FromAddress,
            Destination = new Destination { ToAddresses = [message.Recipient] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    // UTF-8 throughout: subjects and bodies are Arabic for a
                    // large share of users, and the default encoding mangles them.
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = message.Body, Charset = "UTF-8" },
                    },
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(settings.ConfigurationSet))
        {
            request.ConfigurationSetName = settings.ConfigurationSet;
        }

        var response = await ses.SendEmailAsync(request, ct);

        // The SES message id is logged; the recipient is not. Unlike the
        // development stand-in, this runs where logs are kept, and an address in
        // a log line is personal data sitting outside the database.
        logger.LogInformation(
            "Sent a {Channel} notification via SES as {MessageId}", message.Channel, response.MessageId);
    }
}
