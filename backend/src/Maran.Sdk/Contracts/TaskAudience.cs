namespace Maran.Sdk.Contracts;

/// <summary>
/// Who besides an administrator may read a panel task. Stated by the module that records the task,
/// at the moment it records it, and kept apart from the task's account id on purpose: the account id
/// says which account the task CONCERNS, this says who may SEE it, and one column cannot answer both
/// — a suspension concerns the customer's account and is still not theirs to read.
/// </summary>
public enum TaskAudience
{
    /// <summary>Only administrators read the task. The default, and the answer for anything not done for the customer.</summary>
    AdministratorOnly = 0,

    /// <summary>The owner of the task's account reads it too, with its log withheld.</summary>
    AccountOwner = 1,
}
