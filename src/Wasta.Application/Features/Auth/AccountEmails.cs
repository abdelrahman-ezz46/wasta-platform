using Wasta.Application.Features.Notifications;
using Wasta.Domain.Audit;
using Wasta.Domain.Localization;

namespace Wasta.Application.Features.Auth;

/// <summary>
/// Verification and password-reset emails.
///
/// These are sent inline rather than queued through the notification outbox,
/// which is a deliberate exception to how everything else works. The outbox
/// persists a payload, and the payload would have to carry the raw token - so
/// queueing these would put a bearer credential in a database table in plain
/// text, defeating the point of storing only the hash.
///
/// The cost of sending inline is that a delivery failure loses the message. For
/// these two that is acceptable: requesting another link is already the normal
/// path a person takes when one does not arrive.
/// </summary>
public static class AccountEmails
{
    public static (string Subject, string Body) Render(string kind, string link, Language language) =>
        (kind, language) switch
        {
            (NotificationKinds.EmailVerification, Language.Arabic) => (
                "أكّد بريدك الإلكتروني",
                $"أهلاً بك في وسطة. افتح هذا الرابط لتأكيد بريدك الإلكتروني: {link}\n"
                + "ينتهي الرابط خلال ثلاثة أيام. إن لم تنشئ حساباً لدينا، تجاهل هذه الرسالة."),

            (NotificationKinds.EmailVerification, _) => (
                "Confirm your email address",
                $"Welcome to Wasta. Open this link to confirm your email address: {link}\n"
                + "The link expires in three days. If you did not create an account, ignore this message."),

            (NotificationKinds.PasswordReset, Language.Arabic) => (
                "إعادة تعيين كلمة المرور",
                $"افتح هذا الرابط لإعادة تعيين كلمة المرور: {link}\n"
                + "ينتهي الرابط خلال ساعة واحدة. إن لم تطلب ذلك، تجاهل هذه الرسالة "
                + "ولم يطرأ أي تغيير على حسابك."),

            (NotificationKinds.PasswordReset, _) => (
                "Reset your password",
                $"Open this link to reset your password: {link}\n"
                + "The link expires in one hour. If you did not ask for this, ignore this message "
                + "and nothing about your account has changed."),

            _ => ("A message from Wasta", link),
        };

    public static OutboundMessage Message(string kind, string recipient, string link, Language language)
    {
        var (subject, body) = Render(kind, link, language);
        return new OutboundMessage(NotificationChannel.Email, recipient, subject, body);
    }
}
