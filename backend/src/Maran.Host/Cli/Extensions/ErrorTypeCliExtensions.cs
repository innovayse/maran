using Maran.Host.Cli.Enums;
using Maran.SharedKernel.Results;

namespace Maran.Host.Cli.Extensions;

/// <summary>Reads a failure's kind as the status a command-line verb exits with.</summary>
public static class ErrorTypeCliExtensions
{
    /// <summary>Maps a failure's kind to the status the process exits with.</summary>
    /// <param name="type">The failure's kind.</param>
    /// <returns>
    /// <see cref="CliExitCode.Unavailable"/> for an unreachable dependency, <see cref="CliExitCode.Failure"/>
    /// for a fault or an outside party's refusal, otherwise <see cref="CliExitCode.Refused"/>.
    /// </returns>
    public static CliExitCode ToCliExitCode(this ErrorType type)
    {
        return type switch
        {
            ErrorType.Unavailable => CliExitCode.Unavailable,
            ErrorType.UpstreamRefused => CliExitCode.Failure,
            ErrorType.Failure => CliExitCode.Failure,
            _ => CliExitCode.Refused,
        };
    }
}
