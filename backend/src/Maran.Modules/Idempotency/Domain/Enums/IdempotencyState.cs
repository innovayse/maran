namespace Maran.Modules.Idempotency.Domain.Enums;

/// <summary>Where a request that carried an idempotency key stands.</summary>
public enum IdempotencyState
{
    /// <summary>The key is reserved and the request is still running; there is no response to replay.</summary>
    InProgress = 1,

    /// <summary>The request finished; its status, and its body when one was stored, can be replayed.</summary>
    Completed = 2,
}
