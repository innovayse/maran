using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Security;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Demo.Tests.Seeding.Services;

/// <summary>The caller the seed acts as is ambient on one flow, ends with it, and never leaks into a request.</summary>
public sealed class DemoSeedCallerAccessorTests
{
    /// <summary>An administrator to act as.</summary>
    private static readonly AdministratorCaller Administrator = new(Guid.NewGuid(), "demoadmin");

    /// <summary>A reseller to act as inside the administrator.</summary>
    private static readonly ResellerCaller Reseller = new(Guid.NewGuid(), "brightnet", Guid.NewGuid());

    /// <summary>Outside a seed there is no caller.</summary>
    [Fact]
    public void Nobody_is_acted_as_by_default()
    {
        Assert.Null(new DemoSeedCallerAccessor().Current);
    }

    /// <summary>The caller is set while the scope is open and gone after it is disposed.</summary>
    [Fact]
    public void The_caller_ends_with_its_scope()
    {
        var accessor = new DemoSeedCallerAccessor();

        using (accessor.Act(Administrator))
        {
            Assert.Same(Administrator, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    /// <summary>A nested scope restores the outer caller, not nothing.</summary>
    [Fact]
    public void A_nested_scope_restores_the_outer_caller()
    {
        var accessor = new DemoSeedCallerAccessor();

        using var outer = accessor.Act(Administrator);
        using (accessor.Act(Reseller))
        {
            Assert.Same(Reseller, accessor.Current);
        }

        Assert.Same(Administrator, accessor.Current);
    }

    /// <summary>The caller reaches work started after it was set, which is how a handler's own scope sees it.</summary>
    [Fact]
    public async Task The_caller_flows_into_work_started_inside_the_scope()
    {
        var accessor = new DemoSeedCallerAccessor();

        using var acting = accessor.Act(Administrator);
        var seen = await Task.Run(() => { return accessor.Current; });

        Assert.Same(Administrator, seen);
    }

    /// <summary>Work started before the scope opened, as a served request is, never sees the caller.</summary>
    [Fact]
    public async Task Work_started_before_the_scope_never_sees_it()
    {
        var accessor = new DemoSeedCallerAccessor();
        var started = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();

        var request = Task.Run(async () =>
        {
            started.SetResult();
            await proceed.Task;
            return accessor.Current;
        });
        await started.Task;

        using (accessor.Act(Administrator))
        {
            proceed.SetResult();
            Assert.Null(await request);
        }
    }

    /// <summary>
    /// Work started inside the scope that outlives it copied the ambient value; once the scope is disposed that
    /// copy must read nobody, or a long-lived task would keep acting as the administrator (F-4).
    /// </summary>
    [Fact]
    public async Task Work_started_inside_the_scope_reads_nobody_after_it_ended()
    {
        var accessor = new DemoSeedCallerAccessor();
        var disposed = new TaskCompletionSource();
        Task<Caller?> leaked;
        Caller? whileOpen;

        using (accessor.Act(Administrator))
        {
            leaked = Task.Run(async () =>
            {
                await disposed.Task;
                return accessor.Current;
            });
            whileOpen = await Task.Run(() => { return accessor.Current; });
        }

        disposed.SetResult();

        Assert.Same(Administrator, whileOpen);
        Assert.Null(await leaked);
    }

    /// <summary>The control: a copy taken inside a nested scope that is still open keeps reading its caller.</summary>
    [Fact]
    public async Task Work_inside_an_open_scope_keeps_reading_the_caller_while_it_runs()
    {
        var accessor = new DemoSeedCallerAccessor();
        var proceed = new TaskCompletionSource();

        using var acting = accessor.Act(Administrator);
        var work = Task.Run(async () =>
        {
            await proceed.Task;
            return accessor.Current;
        });
        proceed.SetResult();

        Assert.Same(Administrator, await work);
    }

    /// <summary>The current user answers the seed's caller while seeding and the request's own otherwise.</summary>
    [Fact]
    public void The_current_user_follows_the_seed_and_falls_back_to_the_request()
    {
        var accessor = new DemoSeedCallerAccessor();
        var user = new DemoSeedAwareCurrentUser(accessor, new HttpContextCurrentUser(new HttpContextAccessor()));

        Assert.IsType<AnonymousCaller>(user.Caller);

        using (accessor.Act(Administrator))
        {
            Assert.Same(Administrator, user.Caller);
        }

        Assert.IsType<AnonymousCaller>(user.Caller);
    }
}
