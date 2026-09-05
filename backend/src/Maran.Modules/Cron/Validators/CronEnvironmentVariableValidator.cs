using System.Text;
using FluentValidation;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Resources;

namespace Maran.Modules.Cron.Validators;

/// <summary>
/// Validates one <see cref="CronEnvironmentVariableDto"/> before a command carrying it reaches its
/// handler.
/// </summary>
/// <remarks>
/// <para>
/// A mirror of the agent's own <c>EnvVarName</c> and <c>EnvVarValue</c>, and only a mirror: the
/// agent re-validates both halves and its answer decides what is written (rules/architecture.md
/// "Agent").
/// </para>
/// <para>
/// <b>The name is stricter than the command, because unlike the command it really does end up on a
/// line of the crontab.</b> A <c>NAME=value</c> assignment IS the line, so it cannot be moved into a
/// file the way the command was — and it pays for staying there with a permitted alphabet instead of
/// a list of refusals, and with one more refused character in its value.
/// </para>
/// <para>
/// Two names are reserved. A customer who could set <c>MAILTO</c> would have an outbound mail relay
/// through the host's mail transfer agent, and one who could set <c>SHELL</c> would choose the
/// interpreter every one of their entries runs under — including entries created before they changed
/// it. The agent writes both itself, with values of its own choosing.
/// </para>
/// </remarks>
public sealed class CronEnvironmentVariableValidator : AbstractValidator<CronEnvironmentVariableDto>
{
    /// <summary>The most bytes a name may be, matching the agent's ceiling.</summary>
    private const int MaximumNameLengthInBytes = 64;

    /// <summary>The most bytes cron itself keeps from one line of a crontab.</summary>
    /// <remarks>
    /// Both the vixie-cron and the cronie lineage read an environment line into a fixed 1000-byte
    /// buffer and discard the rest SILENTLY — no error, no warning, just a shorter value than the one
    /// on disk. One byte is the terminator, so 999 is what actually survives.
    /// </remarks>
    private const int MaximumCronLineLengthInBytes = 999;

    /// <summary>The most bytes a value may be, derived from what the line leaves.</summary>
    /// <remarks>
    /// Derived rather than chosen, exactly as the agent derives it. The line is
    /// <c>&lt;name&gt;=&lt;value&gt;</c>, so a longer ceiling here would let the panel store and show
    /// a <c>PATH</c> that the host runs truncated — the worst shape a limit can have, because nothing
    /// anywhere reports it.
    /// </remarks>
    private const int MaximumValueLengthInBytes = MaximumCronLineLengthInBytes - MaximumNameLengthInBytes - 1;

    /// <summary>Names the agent writes itself and no customer may set.</summary>
    private static readonly string[] ReservedNames = ["MAILTO", "SHELL"];

    /// <summary>Configures the rules for one environment assignment.</summary>
    public CronEnvironmentVariableValidator()
    {
        RuleFor(variable => variable.Name)
            .Must(name =>
            {
                return IsValidName(name);
            })
            .WithMessage(nameof(ErrorMessages.CronEnvironmentNameInvalid));

        RuleFor(variable => variable.Name)
            .Must(name =>
            {
                return !ReservedNames.Contains(name, StringComparer.Ordinal);
            })
            .WithMessage(nameof(ErrorMessages.CronEnvironmentNameReserved));

        RuleFor(variable => variable.Value)
            .Must(value =>
            {
                return IsValidValue(value);
            })
            .WithMessage(nameof(ErrorMessages.CronEnvironmentValueInvalid));
    }

    /// <summary>Whether a name is one the shell — and this crontab — accepts.</summary>
    /// <param name="candidate">The name as the customer typed it.</param>
    /// <returns>
    /// True for one to sixty-four bytes of uppercase ASCII letters, digits and underscores, not
    /// starting with a digit.
    /// </returns>
    private static bool IsValidName(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(candidate) > MaximumNameLengthInBytes)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        // The alphabet check above already refused everything but A-Z, 0-9 and _, so a first
        // character that is not a letter or an underscore is a digit.
        return !char.IsAsciiDigit(candidate[0]);
    }

    /// <summary>Whether a value survives being written to a crontab line unchanged.</summary>
    /// <param name="candidate">The value as the customer typed it.</param>
    /// <returns>True when cron would store exactly this value rather than a rewritten one.</returns>
    /// <remarks>
    /// An empty value is accepted: <c>TZ=</c> is a real assignment. What is refused is a value cron
    /// would silently ALTER on its way in — it trims whitespace around a value and strips a matching
    /// pair of quotes, so <c>x</c>, <c> x </c> and <c>"x"</c> all set one variable to one thing while
    /// a panel storing all three would show three different values and call two of them wrong. The
    /// percent sign goes for a harder reason: cron rewrites the first unescaped <c>%</c> on a line
    /// into a newline, and this value is on a line.
    /// </remarks>
    private static bool IsValidValue(string? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(candidate) > MaximumValueLengthInBytes)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character) || character == '%')
            {
                return false;
            }
        }

        if (candidate.Length == 0)
        {
            return true;
        }

        if (char.IsWhiteSpace(candidate[0]) || char.IsWhiteSpace(candidate[^1]))
        {
            return false;
        }

        // A one-character value cannot be a pair of quotes around anything, which is what keeps a
        // lone `"` from reading as one.
        return candidate.Length <= 1
            || candidate[0] != candidate[^1]
            || (candidate[0] != '"' && candidate[0] != '\'');
    }
}
