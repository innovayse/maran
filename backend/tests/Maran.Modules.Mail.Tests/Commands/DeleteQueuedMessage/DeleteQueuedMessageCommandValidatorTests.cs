using Maran.Modules.Mail.Commands.DeleteQueuedMessage;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.DeleteQueuedMessage;

/// <summary>Behaviour of the validator that guards a queue id.</summary>
public sealed class DeleteQueuedMessageCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly DeleteQueuedMessageCommandValidator _validator = new();

    /// <summary>Ids of 10 to 25 ASCII letters and digits are accepted, the inverse control for the refusals.</summary>
    /// <param name="queueId">The id.</param>
    [Theory]
    [InlineData("4A1B2C3D4E")]
    [InlineData("4A1B2C3D4E5F")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    public void A_queue_id_of_ten_to_twenty_five_letters_and_digits_is_accepted(string queueId)
    {
        Assert.True(_validator.Validate(Command(queueId)).IsValid);
    }

    /// <summary>Ids that are too short, too long or carry anything but ASCII letters and digits are refused.</summary>
    /// <param name="queueId">The id.</param>
    [Theory]
    [InlineData("")]
    [InlineData("4A1B2C3D4")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("4A1B2C3D4E\n")]
    [InlineData("4A1B2C3D4E ")]
    [InlineData("-4A1B2C3D4E5F")]
    [InlineData("4A1B2C3D/E5F")]
    [InlineData("..4A1B2C3D4E5F")]
    [InlineData("4A1B2C3D4E5١")]
    [InlineData("4A1B2C3D4E5é")]
    public void A_queue_id_of_the_wrong_length_or_alphabet_is_refused(string queueId)
    {
        var result = _validator.Validate(Command(queueId));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == MailServerErrorCodes.MailQueueIdInvalid;
        });
    }

    /// <summary>Builds a command for one queue id.</summary>
    /// <param name="queueId">The id.</param>
    /// <returns>The command.</returns>
    private static DeleteQueuedMessageCommand Command(string queueId)
    {
        return new DeleteQueuedMessageCommand(queueId, MailServerCommands.Address, MailServerCommands.Client);
    }
}
