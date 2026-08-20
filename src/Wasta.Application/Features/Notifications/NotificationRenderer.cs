using System.Text.Json;
using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Notifications;

/// <summary>
/// Turns a stored notification into the words that get sent, in the recipient's
/// own language.
///
/// The language comes from the recipient's saved preference, not from whatever
/// request happened to trigger the notification: a company acting in English
/// must not cause an Arabic-reading student to be emailed in English.
///
/// Payloads carry data, never rendered sentences, which is what makes a second
/// language a matter of adding cases here rather than of re-recording every
/// notification already queued.
/// </summary>
public static class NotificationRenderer
{
    public static (string Subject, string Body) Render(
        string kind, string payloadJson, Language language = Language.English)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;

        return language == Language.Arabic
            ? RenderArabic(kind, payload)
            : RenderEnglish(kind, payload);
    }

    private static (string, string) RenderEnglish(string kind, JsonElement payload) => kind switch
    {
        NotificationKinds.ResultsReady => (
            "Your Wasta Score is ready",
            $"Your assessment has been scored. You scored {Read(payload, "overallPercent")} out of 100. "
            + "Sign in to see your section breakdown and skill gaps."),

        NotificationKinds.ProfileUnlocked => (
            "A company viewed your profile",
            $"{Read(payload, "companyName")} unlocked your profile and can now see your contact "
            + "details. Sign in to see who has viewed you."),

        NotificationKinds.ApplicationStatusChanged => (
            "An update on your application",
            $"Your application for {Read(payload, "jobTitle")} at {Read(payload, "companyName")} "
            + $"is now {Read(payload, "status")}."),

        NotificationKinds.CompanyApproved => (
            "Your company has been verified",
            $"{Read(payload, "companyName")} is verified. Your trial credits are available and you "
            + "can now browse the talent pool."),

        NotificationKinds.CompanyRejected => (
            "We could not verify your company",
            $"We were unable to verify {Read(payload, "companyName")}. {Read(payload, "note")} "
            + "You can upload new documents and we will review again."),

        NotificationKinds.CreditsIssued => (
            "Your credits have been added",
            $"{Read(payload, "credits")} credits have been added to your account following your "
            + $"transfer. Your balance is now {Read(payload, "balance")}."),

        // An unknown kind is still delivered rather than dropped: a missing
        // template is a bug to notice, not a message to lose.
        _ => ("A notification from Wasta", $"You have a new notification ({kind})."),
    };

    private static (string, string) RenderArabic(string kind, JsonElement payload) => kind switch
    {
        NotificationKinds.ResultsReady => (
            "نتيجتك في وسطة جاهزة",
            $"تم تقييم اختبارك. حصلت على {Read(payload, "overallPercent")} من 100. "
            + "سجّل الدخول لعرض تفاصيل الأقسام والفجوات في مهاراتك."),

        NotificationKinds.ProfileUnlocked => (
            "شركة اطّلعت على ملفك",
            $"قامت {Read(payload, "companyName")} بفتح ملفك ويمكنها الآن رؤية بيانات التواصل الخاصة بك. "
            + "سجّل الدخول لمعرفة من اطّلع على ملفك."),

        NotificationKinds.ApplicationStatusChanged => (
            "تحديث على طلبك",
            $"طلبك لوظيفة {Read(payload, "jobTitle")} في {Read(payload, "companyName")} "
            + $"أصبح الآن: {Read(payload, "status")}."),

        NotificationKinds.CompanyApproved => (
            "تم توثيق شركتك",
            $"تم توثيق {Read(payload, "companyName")}. رصيدك التجريبي متاح الآن ويمكنك تصفّح قاعدة المواهب."),

        NotificationKinds.CompanyRejected => (
            "تعذّر توثيق شركتك",
            $"لم نتمكن من توثيق {Read(payload, "companyName")}. {Read(payload, "note")} "
            + "يمكنك رفع مستندات جديدة وسنقوم بالمراجعة مرة أخرى."),

        NotificationKinds.CreditsIssued => (
            "تمت إضافة رصيدك",
            $"تمت إضافة {Read(payload, "credits")} رصيداً إلى حسابك بعد تحويلك. "
            + $"رصيدك الآن {Read(payload, "balance")}."),

        _ => ("إشعار من وسطة", $"لديك إشعار جديد ({kind})."),
    };

    private static string Read(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}
