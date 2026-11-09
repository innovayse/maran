namespace Maran.Host.Authorization.Enums;

/// <summary>Whose API key an authorization policy admits.</summary>
public enum KeyOwner
{
    /// <summary>A key of either owner.</summary>
    Anyone,

    /// <summary>Only a key the administrator owns.</summary>
    Administrator,

    /// <summary>Only a key a reseller owns.</summary>
    Reseller,
}
