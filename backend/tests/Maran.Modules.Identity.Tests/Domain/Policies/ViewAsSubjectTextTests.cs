using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioural contract of <see cref="ViewAsSubjectText"/> (issues #317, #179): the audit subject of
/// a view-as entry names the kind, the viewed identity and the login the same way everywhere.
/// </summary>
public sealed class ViewAsSubjectTextTests
{
    /// <summary>The audit subject names the kind, the viewed identity and the login, for both kinds.</summary>
    [Fact]
    public void The_audit_subject_spells_both_kinds()
    {
        Assert.Equal("account acme as acme-owner", ViewAsSubjectText.Of(ViewAsKind.Account, "acme", "acme-owner"));
        Assert.Equal("reseller northwind as northwind", ViewAsSubjectText.Of(ViewAsKind.Reseller, "northwind", "northwind"));
    }

    /// <summary>A kind the subject text has never heard of is a defect, not a blank.</summary>
    [Fact]
    public void An_undefined_kind_in_the_audit_subject_throws()
    {
        Assert.Throws<UnreachableException>(() =>
        {
            ViewAsSubjectText.Of((ViewAsKind)99, "x", "y");
        });
    }
}
