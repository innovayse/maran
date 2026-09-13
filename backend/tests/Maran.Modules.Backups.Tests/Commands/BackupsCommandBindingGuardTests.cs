using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Modules.Backups.Commands.CreateBackup;
using Maran.Modules.Backups.Commands.RestoreBackup;
using Maran.Modules.Backups.Commands.SaveBackupSchedule;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Backups.Tests.Commands;

/// <summary>
/// What a request body is allowed to fill on this module's body-bound commands, now that the
/// endpoints bind the commands directly and <c>Controllers/Requests/</c> is gone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists beside the panel-wide <c>BoundCommandGuardTests</c>.</b> That test
/// discovers route-supplied members by matching a command's property name against the tokens of the
/// action's route template. This module's route token is <c>id</c> and the property is
/// <c>BackupId</c>, so the names do not match and the panel-wide rule does NOT cover it. The member
/// is still route-supplied, and a body that could fill it would let a caller type one account's name
/// into the confirmation and have a different account's backup restored — the confirmation defeated
/// by the one field it is supposed to be about. The rule is therefore stated here, for this module,
/// where the mismatch is.
/// </para>
/// <para>
/// <b>Both attributes, always.</b> <c>[BindNever]</c> alone does nothing to a JSON body:
/// <c>[FromBody]</c> is read by an input formatter, which never enters the model-binding pipeline
/// <c>[BindNever]</c> belongs to. <c>[JsonIgnore]</c> is what blocks the body; <c>[BindNever]</c>
/// blocks the query and form path for any action that binds the same command from there. They cover
/// disjoint pipelines, so a test that accepted either one would accept the version of this code that
/// reads safe and is not.
/// </para>
/// </remarks>
public sealed class BackupsCommandBindingGuardTests
{
    /// <summary>
    /// The JSON options MVC's input formatter uses for a body, as far as this test needs them:
    /// case-insensitive matching, so a <c>PascalCase</c> body is covered by the same measurement.
    /// One instance, because a new one per call is a per-request reflection cache (CA1869).
    /// </summary>
    private static readonly JsonSerializerOptions BodyOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The members no request body may fill, per command.</summary>
    public static TheoryData<Type, string> GuardedMembers
    {
        get
        {
            return new TheoryData<Type, string>
            {
                { typeof(CreateBackupCommand), nameof(CreateBackupCommand.IpAddress) },
                { typeof(CreateBackupCommand), nameof(CreateBackupCommand.UserAgent) },
                { typeof(RestoreBackupCommand), nameof(RestoreBackupCommand.BackupId) },
                { typeof(RestoreBackupCommand), nameof(RestoreBackupCommand.IpAddress) },
                { typeof(RestoreBackupCommand), nameof(RestoreBackupCommand.UserAgent) },
                { typeof(SaveBackupScheduleCommand), nameof(SaveBackupScheduleCommand.IpAddress) },
                { typeof(SaveBackupScheduleCommand), nameof(SaveBackupScheduleCommand.UserAgent) },
            };
        }
    }

    /// <summary>Every server-established or route-supplied member carries both guards.</summary>
    /// <param name="command">The command type to inspect.</param>
    /// <param name="member">The member that must not be fillable from a request.</param>
    [Theory]
    [MemberData(nameof(GuardedMembers))]
    public void A_server_established_member_carries_both_guards(Type command, string member)
    {
        var property = command.GetProperty(member);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<JsonIgnoreAttribute>());
        Assert.NotNull(property.GetCustomAttribute<BindNeverAttribute>());
    }

    /// <summary>A body naming the guarded members of a restore fills none of them.</summary>
    /// <remarks>
    /// The assertion is on the deserialised value rather than on the attribute, because the attribute
    /// is the mechanism and this is the effect: the attacker's backup id, address and user agent are
    /// all discarded and only the confirmation — the one field that is the caller's to type —
    /// survives. Case-insensitive matching is set because that is what MVC's JSON options do, so a
    /// <c>PascalCase</c> body is covered by the same measurement.
    /// </remarks>
    [Fact]
    public void A_body_cannot_fill_the_guarded_members_of_a_restore()
    {
        const string Body = """
            {"backupId":"99999999-9999-9999-9999-999999999999","confirmAccountUsername":"cust01",
             "ipAddress":"6.6.6.6","userAgent":"ATTACKER"}
            """;

        var command = JsonSerializer.Deserialize<RestoreBackupCommand>(
            Body, BodyOptions);

        Assert.NotNull(command);
        Assert.Equal(Guid.Empty, command!.BackupId);
        Assert.Equal(string.Empty, command.IpAddress);
        Assert.Equal(string.Empty, command.UserAgent);
        Assert.Equal("cust01", command.ConfirmAccountUsername);
    }

    /// <summary>A body naming the guarded members of a create fills neither of them.</summary>
    [Fact]
    public void A_body_cannot_fill_the_guarded_members_of_a_create()
    {
        const string Body = """
            {"accountId":"11111111-1111-1111-1111-111111111111",
             "ipAddress":"6.6.6.6","userAgent":"ATTACKER"}
            """;

        var command = JsonSerializer.Deserialize<CreateBackupCommand>(
            Body, BodyOptions);

        Assert.NotNull(command);
        Assert.Equal(string.Empty, command!.IpAddress);
        Assert.Equal(string.Empty, command.UserAgent);
    }

    /// <summary>
    /// The account a backup is taken of IS the caller's to name, and this states it so the guard is
    /// never widened onto it.
    /// </summary>
    /// <remarks>
    /// This is an administrator's panel: an administrator taking a backup FOR a customer names that
    /// customer in the body, and there is no server-side value to stamp over it with. Which accounts
    /// a caller may name is an authorization question — answered by the module's policy and by
    /// <c>BackupsDbContext</c>'s tenant query filter, which makes another customer's account
    /// not-found — and model binding cannot tell an administrator from a customer. Guarding
    /// <c>AccountId</c> would silently empty it and break the endpoint for everyone.
    /// </remarks>
    [Fact]
    public void The_account_a_backup_names_is_deliberately_not_guarded()
    {
        var accountId = typeof(CreateBackupCommand).GetProperty(nameof(CreateBackupCommand.AccountId));

        Assert.NotNull(accountId);
        Assert.Null(accountId!.GetCustomAttribute<JsonIgnoreAttribute>());

        var command = JsonSerializer.Deserialize<CreateBackupCommand>(
            """{"accountId":"11111111-1111-1111-1111-111111111111"}""",
            BodyOptions);

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), command!.AccountId);
    }
}
