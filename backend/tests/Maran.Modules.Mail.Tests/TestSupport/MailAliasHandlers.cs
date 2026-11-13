using Maran.Modules.Mail.Commands.CreateMailAlias;
using Maran.Modules.Mail.Commands.DeleteMailAlias;
using Maran.Modules.Mail.Commands.UpdateMailAlias;
using Maran.Modules.Mail.Queries.ListMailAliases;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Builds the alias handlers from the parts of a <see cref="MailTestContext"/>.</summary>
public static class MailAliasHandlers
{
    /// <summary>Builds the publisher that reads a domain's alias set and sends it to the recording agent.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The publisher.</returns>
    public static MailAliasSetPublisher Publisher(MailTestContext context)
    {
        return new MailAliasSetPublisher(
            context.Database, context.Agent, NullLogger<MailAliasSetPublisher>.Instance);
    }

    /// <summary>Builds the creation handler.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The handler.</returns>
    public static CreateMailAliasCommandHandler Create(MailTestContext context)
    {
        return new CreateMailAliasCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            Publisher(context),
            context.Journal,
            context.LocalSubmission,
            new AliasKeyLocalizer<DisplayNames>(),
            context.Clock);
    }

    /// <summary>Builds the update handler.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The handler.</returns>
    public static UpdateMailAliasCommandHandler Update(MailTestContext context)
    {
        return new UpdateMailAliasCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            Publisher(context),
            context.Journal,
            context.LocalSubmission,
            new AliasKeyLocalizer<DisplayNames>());
    }

    /// <summary>Builds the deletion handler.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The handler.</returns>
    public static DeleteMailAliasCommandHandler Delete(MailTestContext context)
    {
        return new DeleteMailAliasCommandHandler(
            context.Database, context.Accounts, Publisher(context), context.Journal, context.LocalSubmission);
    }

    /// <summary>Builds the list handler.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The handler.</returns>
    public static ListMailAliasesQueryHandler List(MailTestContext context)
    {
        return new ListMailAliasesQueryHandler(
            context.Database, context.Accounts, new AliasKeyLocalizer<DisplayNames>());
    }
}
