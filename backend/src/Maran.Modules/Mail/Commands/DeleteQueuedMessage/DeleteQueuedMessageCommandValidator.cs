using FluentValidation;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Commands.DeleteQueuedMessage;

/// <summary>
/// Validates <see cref="DeleteQueuedMessageCommand"/>: the queue id is 10 to 25 ASCII letters and
/// digits, the shape of the mail transfer agent's own ids and nothing else.
/// </summary>
/// <remarks>
/// A character sweep and not a pattern, so that no regular-expression anchor can leave a trailing
/// newline through; <see cref="char.IsAsciiLetterOrDigit(char)"/> is ASCII only, which keeps a Unicode
/// digit or letter out. The id reaches the agent's argument vector, where a leading dash or a path
/// separator would be a different command; neither is a letter or a digit.
/// </remarks>
public sealed class DeleteQueuedMessageCommandValidator : AbstractValidator<DeleteQueuedMessageCommand>
{
    /// <summary>The shortest queue id accepted.</summary>
    private const int MinimumLength = 10;

    /// <summary>The longest queue id accepted.</summary>
    private const int MaximumLength = 25;

    /// <summary>Configures the rule for the queue id.</summary>
    public DeleteQueuedMessageCommandValidator()
    {
        RuleFor(command => command.QueueId)
            .Must(BeAQueueId)
            .WithMessage(nameof(ErrorMessages.MailQueueIdInvalid));
    }

    /// <summary>Whether a value has the shape of a queue id.</summary>
    /// <param name="candidate">The id as the route carried it; may be null from a hand-built command.</param>
    /// <returns>True when it is 10 to 25 ASCII letters and digits.</returns>
    private static bool BeAQueueId(string? candidate)
    {
        return candidate is { Length: >= MinimumLength and <= MaximumLength }
            && candidate.All(char.IsAsciiLetterOrDigit);
    }
}
