using Maran.Host.Dashboard;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Dashboard;

/// <summary>
/// Reading one module's section: its answer when it gives one, unavailable when it refuses, fails or is
/// too slow, and the caller's own cancellation told apart from a slow module.
/// </summary>
public sealed class DashboardSectionReaderTests
{
    /// <summary>The bound the slow-module tests use: short enough to wait for, long enough not to be flaky.</summary>
    private static readonly TimeSpan ShortBound = TimeSpan.FromMilliseconds(100);

    /// <summary>A module that answers is a loaded section holding its answer.</summary>
    [Fact]
    public async Task A_module_that_answers_is_a_loaded_section_holding_its_answer()
    {
        var bus = new ScriptedMessageBus
        {
            Responder = (_, _) =>
            {
                return Task.FromResult<object>(Result<int>.Ok(5));
            },
        };

        var section = await DashboardSectionReader.ReadAsync<int>(
            bus, NullLogger.Instance, new object(), CancellationToken.None);

        Assert.Equal(DashboardSectionState.Loaded, section.State);
        Assert.Equal(5, section.Match(data =>
        {
            return data;
        }, () =>
        {
            return -1;
        }));
    }

    /// <summary>A module that refuses is an unavailable section and not a zero.</summary>
    [Fact]
    public async Task A_module_that_refuses_is_an_unavailable_section_and_not_a_zero()
    {
        var bus = new ScriptedMessageBus
        {
            Responder = (_, _) =>
            {
                return Task.FromResult<object>(Result<int>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)));
            },
        };

        var section = await DashboardSectionReader.ReadAsync<int>(
            bus, NullLogger.Instance, new object(), CancellationToken.None);

        Assert.Equal(DashboardSectionState.Unavailable, section.State);
    }

    /// <summary>A module that throws is an unavailable section.</summary>
    [Fact]
    public async Task A_module_that_throws_is_an_unavailable_section()
    {
        var bus = new ScriptedMessageBus
        {
            Responder = (_, _) =>
            {
                throw new InvalidOperationException("the query failed");
            },
        };

        var section = await DashboardSectionReader.ReadAsync<int>(
            bus, NullLogger.Instance, new object(), CancellationToken.None);

        Assert.Equal(DashboardSectionState.Unavailable, section.State);
    }

    /// <summary>A module that never answers is an unavailable section once its time is up.</summary>
    [Fact]
    public async Task A_module_that_never_answers_is_an_unavailable_section_once_its_time_is_up()
    {
        var bus = new ScriptedMessageBus
        {
            Responder = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Result<int>.Ok(1);
            },
        };

        var section = await DashboardSectionReader.ReadAsync<int>(
            bus, NullLogger.Instance, new object(), CancellationToken.None, ShortBound);

        Assert.Equal(DashboardSectionState.Unavailable, section.State);
    }

    /// <summary>The caller giving up is still a cancellation and is not mistaken for a slow module.</summary>
    [Fact]
    public async Task The_caller_giving_up_is_still_a_cancellation_and_not_a_slow_module()
    {
        var bus = new ScriptedMessageBus
        {
            Responder = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Result<int>.Ok(1);
            },
        };
        using var gaveUp = new CancellationTokenSource();
        await gaveUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DashboardSectionReader.ReadAsync<int>(
                bus, NullLogger.Instance, new object(), gaveUp.Token);
        });
    }
}
