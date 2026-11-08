using Maran.Modules.Files.Options;
using Maran.Modules.Files.Services;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Files.Tests.Services;

/// <summary>Holds <see cref="FilesTransferSlots"/> to refusing, not waiting, past its bounds.</summary>
public sealed class FilesTransferSlotsTests
{
    /// <summary>Builds the counter under test.</summary>
    /// <param name="perAccount">The per-account bound.</param>
    /// <param name="global">The panel-wide bound.</param>
    /// <returns>The counter.</returns>
    private static FilesTransferSlots Slots(int perAccount, int global)
    {
        return new FilesTransferSlots(new FixedOptionsMonitor<FilesOptions>(new FilesOptions
        {
            MaxConcurrentTransfersPerAccount = perAccount,
            MaxConcurrentTransfersGlobal = global,
        }));
    }

    /// <summary>An account past its own bound is told one of its transfers is still going.</summary>
    [Fact]
    public void An_account_past_its_bound_is_refused_as_busy()
    {
        var slots = Slots(1, 10);
        var account = Guid.NewGuid();

        using var first = slots.TryAcquire(account).Value;
        var second = slots.TryAcquire(account);

        Assert.False(second.IsSuccess);
        Assert.Equal("FilesAccountBusy", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
    }

    /// <summary>A transfer past the panel-wide bound is refused as a busy server, whoever asks.</summary>
    [Fact]
    public void A_transfer_past_the_panel_wide_bound_is_refused_as_a_busy_server()
    {
        var slots = Slots(5, 1);

        using var first = slots.TryAcquire(Guid.NewGuid()).Value;
        var second = slots.TryAcquire(Guid.NewGuid());

        Assert.Equal("FilesServerBusy", second.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, second.Error.Type);
    }

    /// <summary>A released slot can be taken again, and releasing twice gives back only one.</summary>
    [Fact]
    public void A_released_slot_is_available_again_and_a_double_release_gives_back_one()
    {
        var slots = Slots(1, 10);
        var account = Guid.NewGuid();

        var lease = slots.TryAcquire(account).Value!;
        lease.Dispose();
        lease.Dispose();
        using var again = slots.TryAcquire(account).Value;

        Assert.False(slots.TryAcquire(account).IsSuccess);
    }

    /// <summary>One account's transfers do not use another account's allowance.</summary>
    [Fact]
    public void One_accounts_transfers_do_not_use_anothers_allowance()
    {
        var slots = Slots(1, 10);

        using var first = slots.TryAcquire(Guid.NewGuid()).Value;
        var other = slots.TryAcquire(Guid.NewGuid());

        Assert.True(other.IsSuccess);
        other.Value!.Dispose();
    }
}
