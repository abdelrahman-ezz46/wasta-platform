using System.Text.Json;

namespace Wasta.Application.Features.Notifications;

/// <summary>
/// Turns a stored notification into the words that get sent.
///
/// Kept as one switch rather than scattered through the handlers, so the full
/// set of things the platform says to people is readable in one place. When
/// Arabic lands, this is the seam it goes through: the payload carries data,
/// never a rendered sentence.
/// </summary>
public static class NotificationRenderer
{
    public static (string Subject, string Body) Render(string kind, string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;

        return kind switch
        {
            NotificationKinds.ResultsReady => (
                "Your Wasta Score is ready",
                $"Your assessment has been scored. You scored {Read(payload, "overallPercent")} "
                + "out of 100. Sign in to see your section breakdown and skill gaps."),

            NotificationKinds.ProfileUnlocked => (
                "A company viewed your profile",
                $"{Read(payload, "companyName")} unlocked your profile and can now see your "
                + "contact details. Sign in to see who has viewed you."),

            NotificationKinds.ApplicationStatusChanged => (
                "An update on your application",
                $"Your application for {Read(payload, "jobTitle")} at {Read(payload, "companyName")} "
                + $"is now {Read(payload, "status")}."),

            NotificationKinds.CompanyApproved => (
                "Your company has been verified",
                $"{Read(payload, "companyName")} is verified. Your trial credits are available and "
                + "you can now browse the talent pool."),

            NotificationKinds.CompanyRejected => (
                "We could not verify your company",
                $"We were unable to verify {Read(payload, "companyName")}. "
                + $"{Read(payload, "note")} You can upload new documents and we will review again."),

            NotificationKinds.CreditsIssued => (
                "Your credits have been added",
                $"{Read(payload, "credits")} credits have been added to your account "
                + $"following your transfer. Your balance is now {Read(payload, "balance")}."),

            // An unknown kind still gets delivered rather than silently dropped:
            // a missing template is a bug to notice, not a message to lose.
            _ => ("A notification from Wasta", $"You have a new notification ({kind})."),
        };
    }

    private static string Read(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}
