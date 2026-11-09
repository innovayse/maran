using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Tests.TestSupport;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>The ledger carries one task id across the restart an update causes, and reads as nothing when it is damaged.</summary>
public sealed class PanelUpdateTaskLedgerTests
{
    /// <summary>A written task id is read back, and the ledger is removed on request.</summary>
    [Fact]
    public void A_written_task_id_is_read_back_and_can_be_removed()
    {
        using var world = new UpdatesWorld();
        var id = Guid.NewGuid();

        Assert.Equal(PanelUpdateLedgerOutcome.Written, world.Ledger.TryWrite(id));
        Assert.Equal(id, world.Ledger.TryRead());

        world.Ledger.Remove();
        Assert.Null(world.Ledger.TryRead());
        Assert.Null(world.Ledger.Age());
    }

    /// <summary>A second write leaves the first untouched, so a losing request can never take back the winner's ledger.</summary>
    [Fact]
    public void A_second_write_is_refused_and_leaves_the_first_ledger_as_it_was()
    {
        using var world = new UpdatesWorld();
        var first = Guid.NewGuid();

        Assert.Equal(PanelUpdateLedgerOutcome.Written, world.Ledger.TryWrite(first));
        Assert.Equal(PanelUpdateLedgerOutcome.AlreadyOpen, world.Ledger.TryWrite(Guid.NewGuid()));

        Assert.Equal(first, world.Ledger.TryRead());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(world.Options.TaskLedgerPath)!, "*.tmp"));
    }

    /// <summary>Anything that is not exactly one task id reads as no task, and the file is still an old ledger.</summary>
    [Theory]
    [InlineData("not a guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-2222-4333-8444-555555555555 extra")]
    public void A_garbled_ledger_reads_as_no_task(string text)
    {
        using var world = new UpdatesWorld();
        File.WriteAllText(world.Options.TaskLedgerPath, text);

        Assert.Null(world.Ledger.TryRead());
        Assert.NotNull(world.Ledger.Age());
    }

    /// <summary>An oversize ledger is not read.</summary>
    [Fact]
    public void An_oversize_ledger_is_not_read()
    {
        using var world = new UpdatesWorld();
        File.WriteAllText(world.Options.TaskLedgerPath, Guid.NewGuid() + new string(' ', 4096));

        Assert.Null(world.Ledger.TryRead());
    }

    /// <summary>The ledger's age is measured from its file against the clock.</summary>
    [Fact]
    public void The_age_is_measured_from_the_file()
    {
        using var world = new UpdatesWorld();
        world.Ledger.TryWrite(Guid.NewGuid());
        world.AgeLedger(TimeSpan.FromMinutes(7));

        var age = world.Ledger.Age();

        Assert.NotNull(age);
        Assert.True(Math.Abs((age.GetValueOrDefault() - TimeSpan.FromMinutes(7)).TotalSeconds) < 1);
    }

    /// <summary>A write that cannot be made reports failure and leaves no temporary file behind.</summary>
    [Fact]
    public void A_write_that_cannot_be_made_reports_failure()
    {
        using var world = new UpdatesWorld();
        world.Options.TaskLedgerPath = Path.Combine(world.Options.RequestDirectory, "missing", "ledger");

        Assert.Equal(PanelUpdateLedgerOutcome.Failed, world.Ledger.TryWrite(Guid.NewGuid()));
    }
}
