using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Mappers;

/// <summary>
/// Behavioural contract of <see cref="ViewAsLoginRefusalMapper"/> (issue #416): each refused standing
/// has its own code and every one is a conflict.
/// </summary>
public sealed class ViewAsLoginRefusalMapperTests
{
    /// <summary>An available login is not refused.</summary>
    [Fact]
    public void An_available_login_has_no_refusal()
    {
        Assert.Null(ViewAsLoginRefusalMapper.From(ViewAsLoginStanding.Available));
    }

    /// <summary>A login that has not accepted its invitation has its own code, a conflict.</summary>
    [Fact]
    public void A_pending_invitation_has_its_own_code()
    {
        var error = ViewAsLoginRefusalMapper.From(ViewAsLoginStanding.InvitationPending);

        Assert.Equal("ViewAsInvitationPending", error!.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>A suspended login keeps the code it always had, a conflict.</summary>
    [Fact]
    public void A_suspended_login_keeps_the_login_unavailable_code()
    {
        var error = ViewAsLoginRefusalMapper.From(ViewAsLoginStanding.Unavailable);

        Assert.Equal("ViewAsLoginUnavailable", error!.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>A standing the mapper has never heard of is a defect, not a quiet refusal.</summary>
    [Fact]
    public void An_undefined_standing_throws()
    {
        Assert.Throws<UnreachableException>(() =>
        {
            ViewAsLoginRefusalMapper.From((ViewAsLoginStanding)99);
        });
    }
}
