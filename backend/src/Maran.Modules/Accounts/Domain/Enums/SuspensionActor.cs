namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>Who suspended an account, which decides who may lift the suspension (issue #493).</summary>
public enum SuspensionActor
{
    /// <summary>An administrator suspended the account, for example for abuse. Only an administrator lifts it.</summary>
    Administrator,

    /// <summary>The account's reseller suspended it. The reseller, and an administrator, may lift it.</summary>
    Reseller,
}
