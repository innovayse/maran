using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The transfer failed, or the stream ended without an outcome.</summary>
/// <param name="Error">The typed failure.</param>
/// <param name="Reason">Which connection failure it was; unspecified for anything else.</param>
/// <param name="TransferReason">Which failure of the transfer it was; unspecified for anything else.</param>
public sealed record AgentDatabaseTransferFailed(
    Error Error,
    SourceFailureReason Reason,
    TransferFailureReason TransferReason) : AgentDatabaseTransferEvent;
