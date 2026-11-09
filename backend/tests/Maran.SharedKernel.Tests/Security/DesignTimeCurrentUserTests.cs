using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>What the design-time principal reports to a context built by EF Core's tooling.</summary>
public sealed class DesignTimeCurrentUserTests
{
    /// <summary>The design-time caller is an administrator, so no tenant filter narrows a generated migration.</summary>
    [Fact]
    public void The_design_time_caller_is_an_administrator()
    {
        var caller = new DesignTimeCurrentUser().Caller;

        Assert.IsType<AdministratorCaller>(caller);
    }

    /// <summary>The design-time caller carries a fixed id and name, so it is a value of its own and not an empty stand-in.</summary>
    [Fact]
    public void The_design_time_caller_carries_a_fixed_recognisable_identity()
    {
        var caller = Assert.IsType<AdministratorCaller>(new DesignTimeCurrentUser().Caller);

        Assert.Equal(new Guid("00000000-0000-4000-8000-00000000de51"), caller.UserId);
        Assert.Equal("design-time", caller.Username);
    }
}
