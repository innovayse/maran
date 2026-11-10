namespace Maran.Sdk.Contracts;

/// <summary>Why a lease source refused to hand a lease over: the lease could not be trusted as read.</summary>
public enum LeaseRefusalReason
{
    /// <summary>The lease file is not in a shape the source can read (bad JSON, missing or mistyped field, end before start, too large).</summary>
    Malformed,

    /// <summary>The signature does not match the lease's contents, or was not made by the lease key.</summary>
    SignatureInvalid,

    /// <summary>This build carries no lease verification key, so no lease can be checked.</summary>
    NoVerificationKey,
}
