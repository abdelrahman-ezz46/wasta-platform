using Wasta.Application.Features.Localization;
using Wasta.Domain.Localization;

namespace Wasta.WebApi.Localization;

/// <summary>
/// The language for the current response.
///
/// Taken from an explicit ?lang= if present, otherwise Accept-Language. The
/// account's stored preference deliberately does NOT drive this: it drives
/// notifications, which are sent long after any request header has gone. Making
/// the stored value authoritative here as well would mean a database read on
/// every request to serve something the client already told us.
/// </summary>
public sealed class HttpCurrentLanguage(IHttpContextAccessor accessor) : ICurrentLanguage
{
    public Language Value
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return Languages.Default;
            }

            if (context.Request.Query.TryGetValue("lang", out var explicitLang)
                && !string.IsNullOrWhiteSpace(explicitLang))
            {
                return Languages.Parse(explicitLang!);
            }

            // First tag wins. Full q-value negotiation would be more correct,
            // but with exactly two supported languages it would be ceremony -
            // and anything unrecognised falls back to English anyway.
            var header = context.Request.Headers.AcceptLanguage.FirstOrDefault();

            return string.IsNullOrWhiteSpace(header)
                ? Languages.Default
                : Languages.Parse(header.Split(',')[0]);
        }
    }
}
