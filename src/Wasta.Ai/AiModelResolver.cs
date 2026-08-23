namespace Wasta.Ai;

internal static class AiModelResolver
{
    /// <summary>
    /// A per-feature model override, falling back to the provider's default.
    ///
    /// Empty counts as "not specified", not as a model name. Configuration
    /// binds an absent value to "" rather than null, and the README documents
    /// `"Model": ""` as meaning "use the provider default" - so a plain ?? sends
    /// an empty model name to the provider and gets a 404 that reads as if the
    /// model were deprecated.
    /// </summary>
    internal static string ResolveModel(string? requested, string configured) =>
        string.IsNullOrWhiteSpace(requested) ? configured : requested;
}
