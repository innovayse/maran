using Maran.Modules.Mail.Commands.ChangeMailboxPassword;
using Maran.Modules.Mail.Commands.ClearMailboxAutoresponder;
using Maran.Modules.Mail.Commands.CreateMailbox;
using Maran.Modules.Mail.Commands.DeleteMailbox;
using Maran.Modules.Mail.Commands.SetMailboxAutoresponder;
using Maran.Modules.Mail.Commands.UpdateMailbox;
using Maran.Modules.Mail.Queries.GetMailboxAutoresponder;
using Maran.Modules.Mail.Queries.ListMailboxes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Builds the mailbox and autoresponder handlers from the parts of a <see cref="MailTestContext"/>.</summary>
public static class MailboxGroupHandlers
{
    /// <summary>Builds the create handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static CreateMailboxCommandHandler Create(MailTestContext context)
    {
        return new CreateMailboxCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            context.Agent,
            context.Hashing,
            context.SlotGate,
            context.Journal,
            context.Clock,
            new MailboxGroupDisplayNames(),
            NullLogger<CreateMailboxCommandHandler>.Instance);
    }

    /// <summary>Builds the update handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static UpdateMailboxCommandHandler Update(MailTestContext context)
    {
        return new UpdateMailboxCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            context.Agent,
            context.SlotGate,
            context.Journal,
            new MailboxGroupDisplayNames(),
            NullLogger<UpdateMailboxCommandHandler>.Instance);
    }

    /// <summary>Builds the change-password handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static ChangeMailboxPasswordCommandHandler ChangePassword(MailTestContext context)
    {
        return new ChangeMailboxPasswordCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            context.Agent,
            context.Hashing,
            context.Journal,
            NullLogger<ChangeMailboxPasswordCommandHandler>.Instance);
    }

    /// <summary>Builds the delete handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static DeleteMailboxCommandHandler Delete(MailTestContext context)
    {
        return new DeleteMailboxCommandHandler(
            context.Database, context.Server, context.Accounts, context.Agent, context.Journal);
    }

    /// <summary>Builds the set-autoresponder handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static SetMailboxAutoresponderCommandHandler SetAutoresponder(MailTestContext context)
    {
        return new SetMailboxAutoresponderCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            context.Agent,
            context.Journal,
            NullLogger<SetMailboxAutoresponderCommandHandler>.Instance);
    }

    /// <summary>Builds the clear-autoresponder handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static ClearMailboxAutoresponderCommandHandler ClearAutoresponder(MailTestContext context)
    {
        return new ClearMailboxAutoresponderCommandHandler(
            context.Database, context.Server, context.Accounts, context.Agent, context.Journal);
    }

    /// <summary>Builds the get-autoresponder handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static GetMailboxAutoresponderQueryHandler GetAutoresponder(MailTestContext context)
    {
        return new GetMailboxAutoresponderQueryHandler(context.Database);
    }

    /// <summary>Builds the list handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static ListMailboxesQueryHandler List(MailTestContext context)
    {
        return new ListMailboxesQueryHandler(
            context.Database,
            context.Accounts,
            context.AccountStates,
            context.Agent,
            new MailboxGroupDisplayNames());
    }
}
