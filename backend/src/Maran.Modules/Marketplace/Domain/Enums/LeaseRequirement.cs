namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>Whether a marketplace request needs the licence-lease credential, may carry it, or must not.</summary>
public enum LeaseRequirement
{
    /// <summary>The request never carries the credential (the signing-key list, a package download).</summary>
    None,

    /// <summary>The credential is attached when the panel holds a lease; without one the request goes out anonymously.</summary>
    Optional,

    /// <summary>The request is pointless without the credential, so it is not sent when none can be attached.</summary>
    Required,
}
