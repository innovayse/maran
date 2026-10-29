using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Results;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Break-glass recovery of an administrator who is locked out: issues a one-hour, single-use
/// password-reset link, clears the lockout and ends the administrator's sessions, and prints the link
/// to standard output.
/// </summary>
/// <remarks>
/// <para>
/// <b>The link is the secret.</b> It goes to standard output only, never to a log or to standard
/// error, and it is refused to a pipe or file unless the operator says so with
/// <c>--allow-non-terminal</c>: a reset link in a captured stream is a credential in a file.
/// Everything else the verb says goes to standard error.
/// </para>
/// <para>
/// <b>Who is accountable.</b> The audit row claims the operator named by <c>SUDO_USER</c>, else
/// <c>root</c>. It is a claim and not an authentication: the proof that the caller is root is the
/// wrapper's check, not this process's.
/// </para>
/// <para>
/// Whether the account may be recovered at all (an active administrator, nothing else) is decided by
/// <see cref="AdminRecoveryIssuer"/>, in the module that owns the rule; this verb only carries the
/// request in and the answer out.
/// </para>
/// </remarks>
public sealed class AdminResetPasswordCliCommand : ICliCommand
{
    /// <summary>The flag that also removes the administrator's second factor.</summary>
    private const string ClearTwoFactorFlag = "--clear-two-factor";

    /// <summary>The flag that permits writing the link to a pipe or file.</summary>
    private const string AllowNonTerminalFlag = "--allow-non-terminal";

    /// <summary>The marker after which every argument is the identifier, never a flag.</summary>
    private const string EndOfOptions = "--";

    /// <summary>The operator recorded when sudo did not name one.</summary>
    private const string DefaultOperator = "root";

    /// <summary>Opens the scope the issuer is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Creates the verb.</summary>
    /// <param name="scopeFactory">Opens a scope per run, because the issuer holds a scoped context.</param>
    public AdminResetPasswordCliCommand(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return "admin-reset-password"; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        var arguments = TryParse(invocation.Arguments);
        if (arguments is null)
        {
            await console.Error.WriteLineAsync(
                $"usage: admin-reset-password <email|username> [{ClearTwoFactorFlag}] [{AllowNonTerminalFlag}]");
            return new CliResult(CliExitCode.Usage);
        }

        if (!invocation.StandardOutputIsTerminal && !arguments.AllowNonTerminal)
        {
            await console.Error.WriteLineAsync(
                $"Refusing to print a reset link to something that is not a terminal. Pass {AllowNonTerminalFlag} " +
                "if you mean to capture it.");
            return new CliResult(CliExitCode.Refused);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<AdminRecoveryIssuer>();
        var result = await issuer.IssueAsync(
            arguments.EmailOrUsername,
            arguments.ClearTwoFactor,
            invocation.SudoUser ?? DefaultOperator,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            await console.Error.WriteLineAsync($"Not recovered: {error.Code}.");
            return new CliResult(ExitCodeFor(error.Type));
        }

        var issued = result.Value;
        if (issued is null)
        {
            // No administrator exists at all: there is nobody to recover, and the way in is first-run
            // setup. The setup token is never printed here.
            await console.Error.WriteLineAsync(
                "No administrator exists. Open /setup in a browser and use the Setup__Token from panel.env.");
            return new CliResult(CliExitCode.Refused);
        }

        await console.Output.WriteLineAsync(issued.Link.Reveal());
        await console.Error.WriteLineAsync(
            $"Reset link issued for {issued.Username}. It works once and expires at {issued.ExpiresAt:u}.");
        if (issued.LinkIsRelative)
        {
            await console.Error.WriteLineAsync(
                "No public address is configured, so the link is a path: put the address you reach the panel at " +
                "in front of it.");
        }

        if (issued.TwoFactorCleared)
        {
            await console.Error.WriteLineAsync(
                "Two-factor authentication was removed; the administrator must enrol again.");
        }

        return new CliResult(CliExitCode.Success);
    }

    /// <summary>Reads the arguments, or returns null when they are not a valid request.</summary>
    /// <param name="arguments">Everything after the verb.</param>
    /// <returns>The parsed request, or null for a missing, repeated or unknown argument.</returns>
    private static AdminResetPasswordArguments? TryParse(IReadOnlyList<string> arguments)
    {
        string? target = null;
        var clearTwoFactor = false;
        var allowNonTerminal = false;
        var endOfOptions = false;

        foreach (var argument in arguments)
        {
            // `--` ends the options: the installed wrapper always passes it before the identifier, so an
            // identifier that starts with a dash is never read as a flag. Refusing it as an unknown
            // option made every real invocation a usage error (#68's real-install run found it).
            if (endOfOptions)
            {
                if (target is not null)
                {
                    return null;
                }

                target = argument;
            }
            else if (string.Equals(argument, EndOfOptions, StringComparison.Ordinal))
            {
                endOfOptions = true;
            }
            else if (string.Equals(argument, ClearTwoFactorFlag, StringComparison.Ordinal))
            {
                clearTwoFactor = true;
            }
            else if (string.Equals(argument, AllowNonTerminalFlag, StringComparison.Ordinal))
            {
                allowNonTerminal = true;
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal) || target is not null)
            {
                return null;
            }
            else
            {
                target = argument;
            }
        }

        return string.IsNullOrWhiteSpace(target)
            ? null
            : new AdminResetPasswordArguments(target, clearTwoFactor, allowNonTerminal);
    }

    /// <summary>Maps a failure's kind to the status the process exits with.</summary>
    /// <param name="type">The failure's kind.</param>
    /// <returns>
    /// <see cref="CliExitCode.Unavailable"/> for an unreachable dependency, <see cref="CliExitCode.Failure"/>
    /// for a fault, otherwise <see cref="CliExitCode.Refused"/>.
    /// </returns>
    private static CliExitCode ExitCodeFor(ErrorType type)
    {
        return type switch
        {
            ErrorType.Unavailable => CliExitCode.Unavailable,
            ErrorType.Failure => CliExitCode.Failure,
            _ => CliExitCode.Refused,
        };
    }
}
