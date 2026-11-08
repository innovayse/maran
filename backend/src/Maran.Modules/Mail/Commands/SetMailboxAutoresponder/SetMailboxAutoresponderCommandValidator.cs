using System.Text;
using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.SetMailboxAutoresponder;

/// <summary>
/// Validates <see cref="SetMailboxAutoresponderCommand"/> before it reaches the handler (rules/security.md
/// "Input" and item 4).
/// </summary>
/// <remarks>
/// <para>
/// The subject and the body end up in a mail-filtering script the host renders and runs, so this is
/// config-file injection by another name: a value is validated, never escaped. A control character, a
/// line that would end the script's text block (a line beginning with a full stop) and a variable
/// expansion (<c>${</c>) are refused. The agent re-validates the same rules; this is the first boundary.
/// </para>
/// <para>
/// Every refusal answers one code, <c>MailAutoresponderInvalid</c>, because the customer's remedy is the
/// same sentence for all of them: change the text or the dates.
/// </para>
/// </remarks>
public sealed class SetMailboxAutoresponderCommandValidator : AbstractValidator<SetMailboxAutoresponderCommand>
{
    /// <summary>Configures the field rules for <see cref="SetMailboxAutoresponderCommand"/>.</summary>
    public SetMailboxAutoresponderCommandValidator()
    {
        RuleFor(command => command.Subject)
            .Must(IsAcceptableSubject)
            .WithMessage(nameof(ErrorMessages.MailAutoresponderInvalid));

        RuleFor(command => command.Body)
            .Must(IsAcceptableBody)
            .WithMessage(nameof(ErrorMessages.MailAutoresponderInvalid));

        RuleFor(command => command.EndsAt)
            .Must((command, endsAt) =>
            {
                return endsAt is null || command.StartsAt is null || endsAt > command.StartsAt;
            })
            .WithMessage(nameof(ErrorMessages.MailAutoresponderInvalid));

        RuleFor(command => command.ReplyIntervalDays)
            .Must(interval =>
            {
                return interval is null or >= 1 and <= MailDefaults.AutoresponderMaxReplyIntervalDays;
            })
            .WithMessage(nameof(ErrorMessages.MailAutoresponderInvalid));
    }

    /// <summary>Whether a subject line is something the host may be given.</summary>
    /// <param name="subject">The subject as sent.</param>
    /// <returns><c>true</c> when it is non-empty, short enough and free of control characters and <c>${</c>.</returns>
    private static bool IsAcceptableSubject(string? subject)
    {
        return !string.IsNullOrEmpty(subject)
            && subject.Length <= MailDefaults.AutoresponderSubjectMaxLength
            && !subject.Any(char.IsControl)
            && !subject.Contains("${", StringComparison.Ordinal);
    }

    /// <summary>Whether a body is something the host may be given.</summary>
    /// <param name="body">The body as sent.</param>
    /// <returns>
    /// <c>true</c> when it is non-empty, within the byte ceiling, free of control characters other than line
    /// feed, has no line beginning with a full stop and contains no <c>${</c>.
    /// </returns>
    private static bool IsAcceptableBody(string? body)
    {
        if (string.IsNullOrEmpty(body)
            || Encoding.UTF8.GetByteCount(body) > MailDefaults.AutoresponderBodyMaxBytes
            || body.Contains("${", StringComparison.Ordinal))
        {
            return false;
        }

        var hasForbiddenControl = body.Any(character =>
        {
            return char.IsControl(character) && character != '\n';
        });
        if (hasForbiddenControl)
        {
            return false;
        }

        return !body.Split('\n').Any(line =>
        {
            return line.StartsWith('.');
        });
    }
}
