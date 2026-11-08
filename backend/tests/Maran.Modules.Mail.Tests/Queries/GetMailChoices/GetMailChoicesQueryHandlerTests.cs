using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Queries.GetMailChoices;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.GetMailChoices;

/// <summary>The lists and bounds the mail screens are built from, for a customer and for anyone else.</summary>
public sealed class GetMailChoicesQueryHandlerTests
{
    /// <summary>The account whose plan the customer in these tests is held to.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>The lists carry camelCase values and the display-name keys of every member.</summary>
    [Fact]
    public async Task The_lists_carry_camel_case_values_and_localized_labels()
    {
        using var context = new MailTestContext();

        var result = await DomainGroupHandlers.Choices(context).HandleAsync(
            new GetMailChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["active", "suspended"],
            result.Value.DomainStatuses.Select(choice =>
            {
                return choice.Value;
            }).ToArray());
        Assert.Equal(
            ["MailDomainStatusActive", "MailDomainStatusSuspended"],
            result.Value.DomainStatuses.Select(choice =>
            {
                return choice.Label;
            }).ToArray());
        Assert.Equal(
            ["active", "suspended", "disabled"],
            result.Value.MailboxStatuses.Select(choice =>
            {
                return choice.Value;
            }).ToArray());
        Assert.Equal(
            ["alias", "forwarder", "catchAll"],
            result.Value.AliasKinds.Select(choice =>
            {
                return choice.Value;
            }).ToArray());
        Assert.Equal("MailAliasKindCatchAll", result.Value.AliasKinds[2].Label);
    }

    /// <summary>A customer is held to their own plan's figures.</summary>
    [Fact]
    public async Task A_customer_is_held_to_their_own_plans_figures()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme", new MailLimitsSnapshot(5, 2048, 250, 4096));

        var result = await DomainGroupHandlers.Choices(context).HandleAsync(
            new GetMailChoicesQuery(), CancellationToken.None);

        var limits = result.Value.Limits;
        Assert.Equal(1, limits.MinimumQuotaMib);
        Assert.Equal(4096, limits.MaximumQuotaMib);
        Assert.Equal(2048, limits.DefaultQuotaMib);
        Assert.Equal(250, limits.MaximumSendLimitPerHour);
        Assert.Equal(250, limits.DefaultSendLimitPerHour);
        Assert.Equal(MailDefaults.PasswordMinLength, limits.MinimumPasswordLength);
        Assert.Equal(MailDefaults.MaxAliasTargets, limits.MaximumAliasTargets);
    }

    /// <summary>A plan pool above the module's hard maximum is capped at that maximum.</summary>
    [Fact]
    public async Task A_pool_above_the_hard_maximum_is_capped()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme", new MailLimitsSnapshot(5, 2048, 250, MailDefaults.MaxMailboxQuotaMib * 2));

        var result = await DomainGroupHandlers.Choices(context).HandleAsync(
            new GetMailChoicesQuery(), CancellationToken.None);

        Assert.Equal(MailDefaults.MaxMailboxQuotaMib, result.Value.Limits.MaximumQuotaMib);
    }

    /// <summary>A customer whose account cannot be read is answered account-not-found.</summary>
    [Fact]
    public async Task A_customer_whose_account_cannot_be_read_is_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());

        var result = await DomainGroupHandlers.Choices(context).HandleAsync(
            new GetMailChoicesQuery(), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>An administrator is offered the default allowances and the module's hard maxima.</summary>
    [Fact]
    public async Task An_administrator_is_offered_the_defaults_and_the_hard_maxima()
    {
        using var context = new MailTestContext();

        var result = await DomainGroupHandlers.Choices(context).HandleAsync(
            new GetMailChoicesQuery(), CancellationToken.None);

        var limits = result.Value.Limits;
        Assert.Equal(MailLimitsSnapshot.Default.DefaultMailboxQuotaMib, limits.DefaultQuotaMib);
        Assert.Equal(MailLimitsSnapshot.Default.MailboxSendLimitPerHour, limits.DefaultSendLimitPerHour);
        Assert.Equal(MailDefaults.MaxMailboxQuotaMib, limits.MaximumQuotaMib);
        Assert.Equal(MailDefaults.MaxSendLimitPerHour, limits.MaximumSendLimitPerHour);
    }
}
