namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// Why a pull of files or a transfer of a database failed, for the cases the panel tells apart and acts on
/// differently (issue #197, phase 2). A connection failure has its own <see cref="SourceFailureReason"/>
/// beside it.
/// </summary>
public enum TransferFailureReason
{
    /// <summary>No reason the panel tells apart: success, or any other failure.</summary>
    Unspecified = 0,

    /// <summary>The target host has no room for the transfer.</summary>
    DiskInsufficient = 1,

    /// <summary>The copy ended with a failure status.</summary>
    PullFailed = 2,

    /// <summary>The pull used up its time allowance without finishing.</summary>
    PullIncomplete = 3,

    /// <summary>The dump command on the source ended with a failure status.</summary>
    DumpFailed = 4,

    /// <summary>The dump is larger than the allowance the request stated.</summary>
    DumpTooLarge = 5,

    /// <summary>The restricted loader refused the dump (a trigger, a routine, a view, another database's name).</summary>
    LoadRefused = 6,

    /// <summary>The server refused a statement of the dump, or the load could not run.</summary>
    LoadFailed = 7,
}
