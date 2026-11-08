using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Queries.GetAlertRecipients;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Queries.GetAlertRecipients;

/// <summary>Reading the alert recipient list back.</summary>
public sealed class GetAlertRecipientsQueryHandlerTests
{
    /// <summary>A fresh panel reads an empty list rather than a failure.</summary>
    [Fact]
    public async Task A_fresh_panel_reads_an_empty_list()
    {
        await using var dbContext = NotificationsTestContext.Create();

        var result = await new GetAlertRecipientsQueryHandler(dbContext)
            .HandleAsync(new GetAlertRecipientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Addresses);
    }

    /// <summary>The list reads back in its stored order.</summary>
    [Fact]
    public async Task The_list_reads_back_in_its_stored_order()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var at = new FakeClock().UtcNow;
        dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), "b@example.com", 1, at));
        dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), "a@example.com", 0, at));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new GetAlertRecipientsQueryHandler(dbContext)
            .HandleAsync(new GetAlertRecipientsQuery(), CancellationToken.None);

        Assert.Equal(["a@example.com", "b@example.com"], result.Value.Addresses);
    }

    /// <summary>The read carries the cap the save enforces, so the screen holds no constant of its own.</summary>
    [Fact]
    public async Task The_read_carries_the_cap_the_save_enforces()
    {
        await using var dbContext = NotificationsTestContext.Create();

        var result = await new GetAlertRecipientsQueryHandler(dbContext)
            .HandleAsync(new GetAlertRecipientsQuery(), CancellationToken.None);

        Assert.Equal(AlertRecipient.MaxCount, result.Value.Maximum);
    }
}
