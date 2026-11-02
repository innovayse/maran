using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The principal handed to a module's <c>DbContext</c> while its MODEL is being built, and nothing
/// more.
/// </summary>
/// <remarks>
/// A context closes its global query filter over <see cref="ICurrentUser"/>, so one must exist
/// before <c>OnModelCreating</c> can run at all. The values are never read: these tests ask whether
/// a filter was REGISTERED, not what it evaluates to, and a filter's expression is captured once
/// per model rather than per query. It reports a customer rather than an administrator so that a
/// context whose filter short-circuits on an administrator caller still registers the whole expression.
/// </remarks>
public sealed class ArchitectureCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; } = new CustomerCaller(
        new Guid("00000000-0000-4000-8000-00000000a4c1"),
        "architecture",
        new Guid("00000000-0000-4000-8000-00000000a4c2"));
}
