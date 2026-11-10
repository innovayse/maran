using Maran.Host.Security;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.Security;

/// <summary>
/// The job-caller primitive (issue #127): a durable job acts as an administrator for the length of one scope and
/// as nobody outside it. It is an impersonation facility, so what it does NOT do is tested as hard as what it does.
/// </summary>
public sealed class JobCallerAccessorTests
{
    /// <summary>An administrator for the tests to act as.</summary>
    private static readonly AdministratorCaller Admin = new(Guid.NewGuid(), "admin");

    /// <summary>Outside any scope nobody is acting.</summary>
    [Fact]
    public void Outside_a_scope_no_caller_is_assigned()
    {
        Assert.Null(new JobCallerAccessor().Current);
    }

    /// <summary>The inverse control: inside a scope the administrator is the caller.</summary>
    [Fact]
    public void Inside_a_scope_the_administrator_is_the_caller()
    {
        var accessor = new JobCallerAccessor();

        using (accessor.Act(Admin))
        {
            Assert.Same(Admin, accessor.Current);
        }
    }

    /// <summary>Disposing the scope ends it.</summary>
    [Fact]
    public void After_the_scope_ends_no_caller_is_assigned()
    {
        var accessor = new JobCallerAccessor();

        accessor.Act(Admin).Dispose();

        Assert.Null(accessor.Current);
    }

    /// <summary>
    /// Work started inside the scope copies the ambient value; it must stop acting as the administrator when the
    /// scope ends, or a task that outlives its job keeps the authority for good.
    /// </summary>
    [Fact]
    public async Task Work_that_copied_the_scope_stops_acting_when_the_scope_ends()
    {
        var accessor = new JobCallerAccessor();
        var ended = new TaskCompletionSource();
        var scope = accessor.Act(Admin);
        var copied = Task.Run(async () =>
        {
            await ended.Task;

            return accessor.Current;
        });

        scope.Dispose();
        ended.SetResult();

        Assert.Null(await copied);
    }

    /// <summary>The current user is the job's administrator while a scope is open.</summary>
    [Fact]
    public void The_current_user_is_the_job_caller_while_a_scope_is_open_and_anonymous_otherwise()
    {
        var accessor = new JobCallerAccessor();
        var user = new JobAwareCurrentUser(accessor, new HttpContextCurrentUser(new HttpContextAccessor()));

        Assert.IsType<AnonymousCaller>(user.Caller);

        using (accessor.Act(Admin))
        {
            Assert.Same(Admin, user.Caller);
        }

        Assert.IsType<AnonymousCaller>(user.Caller);
    }

    /// <summary>An opener whose source finds no active administrator opens no scope.</summary>
    [Fact]
    public async Task A_user_who_is_not_an_active_administrator_gets_no_scope()
    {
        var accessor = new JobCallerAccessor();
        var none = new JobCallerScopes(new FixedAdministrators(null), accessor);
        var some = new JobCallerScopes(new FixedAdministrators(Admin), accessor);

        Assert.Null(await none.BeginAsync(Guid.NewGuid(), CancellationToken.None));
        using var scope = await some.BeginAsync(Admin.UserId, CancellationToken.None);

        Assert.NotNull(scope);
        Assert.Same(Admin, accessor.Current);
    }

    /// <summary>A source of administrators with a fixed answer.</summary>
    private sealed class FixedAdministrators : Maran.Sdk.Interfaces.IAdministratorCallerSource
    {
        /// <summary>What every ask answers.</summary>
        private readonly AdministratorCaller? _answer;

        /// <summary>Creates the source.</summary>
        /// <param name="answer">What every ask answers.</param>
        public FixedAdministrators(AdministratorCaller? answer)
        {
            _answer = answer;
        }

        /// <inheritdoc />
        public Task<AdministratorCaller?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_answer);
        }
    }
}
