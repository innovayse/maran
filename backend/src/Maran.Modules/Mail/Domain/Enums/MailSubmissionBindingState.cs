namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>What the panel knows about the local submission binding the agent holds for one system user.</summary>
public enum MailSubmissionBindingState
{
    /// <summary>The agent holds the binding the panel last wanted for the account.</summary>
    Bound,

    /// <summary>The agent holds a binding that differs from what the account's rows and plan now say; a push is owed.</summary>
    OutOfDate,

    /// <summary>The agent holds no binding and none is wanted: the account is suspended or being deleted.</summary>
    Withdrawn,

    /// <summary>The agent may still hold a binding that is no longer wanted; a removal is owed.</summary>
    WithdrawalPending,
}
