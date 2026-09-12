using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;

namespace Maran.Modules.Ftp.Tests.Domain.Entities;

/// <summary>
/// The shape of a stored FTPS login, asserted on the one axis that can silently stop being true.
/// </summary>
public sealed class FtpUserTests
{
    /// <summary>The stored login has no password-shaped member of any kind.</summary>
    /// <remarks>
    /// <para>
    /// Every other assertion about the password is a probe over VALUES — serialize the row, look for
    /// the string — and every one of them is vacuous while no such property exists, which is exactly
    /// the state this design intends. So this is the guard on the axis those probes cannot see: it
    /// asserts the absence directly, and it is what goes red the day somebody adds a
    /// <c>PasswordHash</c> for the best of reasons.
    /// </para>
    /// <para>
    /// Why the absence matters: a stored copy, however it is encrypted, is a copy that can be read
    /// out of the panel's database, and a panel that can read back every customer's FTPS password is
    /// a single theft away from every customer's files.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_stored_login_has_no_password_shaped_member()
    {
        var properties = typeof(FtpUser).GetProperties();

        Assert.NotEmpty(properties);
        Assert.DoesNotContain(properties, property =>
        {
            return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>The probe can see a password-shaped member, so a green result means it looked.</summary>
    /// <remarks>
    /// The positive control the sweep above owes (rules/testing.md). Without it, a name filter that
    /// matched nothing under any circumstances would certify the absence of a defect it could not
    /// have detected. <see cref="CreatedFtpUserDto"/> is a type in this module that DOES carry a
    /// password, so it is the honest planted value rather than a fabricated one.
    /// </remarks>
    [Fact]
    public void The_password_member_probe_finds_one_where_a_password_really_is_carried()
    {
        Assert.Contains(typeof(CreatedFtpUserDto).GetProperties(), property =>
        {
            return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>The stored login has no jail or path member either.</summary>
    /// <remarks>
    /// The second absence the design rests on: the agent derives the jail from the validated account
    /// name and creates it root-owned, so the customer names no directory. A column here would be
    /// something for a request to fill.
    /// </remarks>
    [Fact]
    public void The_stored_login_has_no_path_shaped_member()
    {
        Assert.DoesNotContain(typeof(FtpUser).GetProperties(), property =>
        {
            return property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Jail", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Home", StringComparison.OrdinalIgnoreCase);
        });
    }
}
