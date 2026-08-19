namespace Wasta.Domain.Common;

/// <summary>
/// A business rule was violated. Distinct from a validation failure: validation
/// rejects a malformed request, this rejects a well-formed request that the
/// current state does not allow.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable machine-readable code. Clients switch on this, never on the message.</summary>
    public string Code { get; }
}
