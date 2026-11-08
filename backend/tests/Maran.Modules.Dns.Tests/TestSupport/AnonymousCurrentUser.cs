using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// A principal that is nobody, as unattended work at start-up is: the tenant filter shows it no zone, which
/// is what a start-up pass has to see past on purpose.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <summary>The anonymous caller.</summary>
    public Caller Caller { get; } = AnonymousCaller.Instance;
}
