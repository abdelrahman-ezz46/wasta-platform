namespace Wasta.WebApi.Integration;

/// <summary>
/// The AI modules were written before the platform and type their ids as
/// <c>int</c>; the platform uses <c>long</c>. Every crossing goes through here
/// rather than through a scattered cast.
///
/// An out-of-range id is a real failure, not something to wrap silently: an
/// unchecked cast would quietly hand the module a different student's id.
/// Practically this ceiling is 2.1 billion rows away, but the conversion is
/// still checked, because "cannot happen" is how the worst bugs are described
/// afterwards.
/// </summary>
public static class PlatformIds
{
    public static int ToModuleId(long id, string what) =>
        id is >= int.MinValue and <= int.MaxValue
            ? (int)id
            : throw new InvalidOperationException(
                $"{what} {id} does not fit the AI modules' 32-bit id. Widen the module before this "
                + "becomes reachable.");

    public static int? TryToModuleId(long? id) =>
        id is null || id.Value is < int.MinValue or > int.MaxValue ? null : (int)id.Value;
}
