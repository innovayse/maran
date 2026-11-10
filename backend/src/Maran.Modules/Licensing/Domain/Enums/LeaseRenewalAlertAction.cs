namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>What the operator is to be told after a renewal pass.</summary>
public enum LeaseRenewalAlertAction
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Renewals have failed repeatedly: raise the alert.</summary>
    Raise,

    /// <summary>A renewal succeeded after the alert was raised: say it is over.</summary>
    Resolve,
}
