using System.Globalization;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Queries.ListSmtpSecurityChoices;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Queries.ListSmtpSecurityChoices;

/// <summary>What the settings form's protection picker is offered: every mode once, named in the caller's language.</summary>
public sealed class ListSmtpSecurityChoicesQueryHandlerTests
{
    /// <summary>The picker is offered every member of the enum exactly once, in declaration order.</summary>
    [Fact]
    public async Task Every_protection_mode_is_offered_once_in_declaration_order()
    {
        var handler = new ListSmtpSecurityChoicesQueryHandler(NotificationsTestContext.SecurityNames());

        var result = await handler.HandleAsync(new ListSmtpSecurityChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Enum.GetValues<SmtpSecurity>(),
            result.Value.Select(choice =>
            {
                return choice.Security;
            }));
    }

    /// <summary>Each mode carries a real name in all three languages, never the resource key.</summary>
    /// <param name="culture">The request culture to name the modes in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_protection_mode_has_a_name_in_each_language(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var handler = new ListSmtpSecurityChoicesQueryHandler(NotificationsTestContext.SecurityNames());

            var result = await handler.HandleAsync(new ListSmtpSecurityChoicesQuery(), CancellationToken.None);

            foreach (var choice in result.Value)
            {
                Assert.False(string.IsNullOrWhiteSpace(choice.SecurityName));
                Assert.DoesNotContain("SmtpSecurity", choice.SecurityName, StringComparison.Ordinal);
            }

            Assert.Equal(
                result.Value.Count,
                result.Value.Select(choice =>
                {
                    return choice.SecurityName;
                }).Distinct().Count());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
