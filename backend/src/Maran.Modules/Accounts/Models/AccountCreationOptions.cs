using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Models;

/// <summary>
/// What the creator chose for a new account beyond its name, domain and plan, as the audit journal
/// lists it. Carries no password: the journal is never deleted, and the mode alone says how the owner
/// was given a way in.
/// </summary>
/// <param name="LoginMode">How the owner gets a way in.</param>
/// <param name="ShellAccess">Whether the system user was given a login shell.</param>
/// <param name="BackupEnabled">Whether the account is included in scheduled backups.</param>
/// <param name="Limits">The ceilings the account states for itself; empty when it follows its plan.</param>
public sealed record AccountCreationOptions(
    OwnerLoginMode LoginMode,
    bool ShellAccess,
    bool BackupEnabled,
    AccountLimitOverrides Limits);
