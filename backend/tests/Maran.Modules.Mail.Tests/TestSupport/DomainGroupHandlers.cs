using Maran.Modules.Mail.Commands.CreateMailDomain;
using Maran.Modules.Mail.Commands.DeleteMailDomain;
using Maran.Modules.Mail.Commands.RotateDkimKey;
using Maran.Modules.Mail.Queries.GetMailChoices;
using Maran.Modules.Mail.Queries.GetMailDomainDnsRecords;
using Maran.Modules.Mail.Queries.ListMailDomains;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Builds the mail-domain handlers from the parts of a <see cref="MailTestContext"/>.</summary>
public static class DomainGroupHandlers
{
    /// <summary>Builds the create handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static CreateMailDomainCommandHandler Create(MailTestContext context)
    {
        return new CreateMailDomainCommandHandler(
            context.Database,
            context.Server,
            context.Accounts,
            context.Sites,
            context.Agent,
            context.Keys,
            context.Encryption,
            context.Journal,
            context.Clock,
            new DomainGroupDisplayNames(),
            NullLogger<CreateMailDomainCommandHandler>.Instance);
    }

    /// <summary>Builds the delete handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static DeleteMailDomainCommandHandler Delete(MailTestContext context)
    {
        return new DeleteMailDomainCommandHandler(context.Database, context.Accounts, context.Agent, context.Journal);
    }

    /// <summary>Builds the key-rotation handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static RotateDkimKeyCommandHandler Rotate(MailTestContext context)
    {
        return new RotateDkimKeyCommandHandler(
            context.Database,
            context.Accounts,
            context.Agent,
            context.Keys,
            context.Encryption,
            context.Journal,
            context.Clock,
            new DomainGroupDisplayNames(),
            NullLogger<RotateDkimKeyCommandHandler>.Instance);
    }

    /// <summary>Builds the list handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static ListMailDomainsQueryHandler List(MailTestContext context)
    {
        return new ListMailDomainsQueryHandler(
            context.Database, context.Accounts, context.AccountStates, new DomainGroupDisplayNames());
    }

    /// <summary>Builds the DNS-records handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static GetMailDomainDnsRecordsQueryHandler DnsRecords(MailTestContext context)
    {
        return new GetMailDomainDnsRecordsQueryHandler(context.Database, context.Server, new DomainGroupDisplayNames());
    }

    /// <summary>Builds the choices handler.</summary>
    /// <param name="context">The test context whose parts the handler is wired to.</param>
    /// <returns>The handler.</returns>
    public static GetMailChoicesQueryHandler Choices(MailTestContext context)
    {
        return new GetMailChoicesQueryHandler(context.Accounts, context.Principal, new DomainGroupDisplayNames());
    }
}
