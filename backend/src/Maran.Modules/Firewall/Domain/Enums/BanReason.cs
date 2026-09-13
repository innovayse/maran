namespace Maran.Modules.Firewall.Domain.Enums;

/// <summary>Why an address was banned from the host.</summary>
/// <remarks>
/// The reason lives on this side and only on this side. The agent stores none, because the only
/// place one could go there is an nftables comment, whose argument <c>nft</c> parses in its own
/// grammar — an injection primitive for a string the panel composes. So a ban read back from the
/// kernel is an address and a countdown and nothing else, and this enum is the whole of the
/// product's answer to "why is this customer's office cut off".
/// </remarks>
public enum BanReason
{
    /// <summary>An administrator asked for it from the panel.</summary>
    Manual = 1,

    /// <summary>Repeated failed sign-ins from the address crossed the brute-force threshold.</summary>
    BruteForce = 2,
}
