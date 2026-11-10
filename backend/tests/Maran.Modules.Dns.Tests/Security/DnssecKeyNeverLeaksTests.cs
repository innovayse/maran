using System.Reflection;
using System.Text.Json;
using Maran.Modules.Dns.Commands.EnableZoneSigning;
using Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;
using Maran.Modules.Dns.Commands.WithdrawZoneSigning;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetZone;
using Maran.Modules.Dns.Queries.GetZoneDnssec;
using Maran.Modules.Dns.Services;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Security;

/// <summary>
/// The signing key's private half leaves the panel in exactly one direction, to the agent, and appears nowhere
/// else: not in a DTO, not in a serialised answer, not in an audit row, not in a log line, not in a failure.
/// Each search names what it looked in and carries a positive control that plants the value where the search
/// looks, because a search that finds nothing is only worth anything if it could have found something.
/// </summary>
public sealed class DnssecKeyNeverLeaksTests
{
    /// <summary>The words a property that carries the key would be named with.</summary>
    private static readonly string[] SecretWords = ["private", "secret", "scalar"];

    /// <summary>The DTOs this module puts on the wire.</summary>
    private static readonly Type[] WireTypes = typeof(DnsModule).Assembly.GetTypes()
        .Where(type =>
        {
            return type.Namespace == "Maran.Modules.Dns.Common" && type.Name.EndsWith("Dto", StringComparison.Ordinal);
        })
        .ToArray();

    /// <summary>A stand-in for a DTO that would leak, used only to prove the reflection search can flag one.</summary>
    /// <param name="Name">An innocent member.</param>
    /// <param name="PrivateKey">The member a leak would add.</param>
    private sealed record PlantedLeakyDto(string Name, string PrivateKey);

    /// <summary>Lists the members of the given types whose names say they carry a secret.</summary>
    /// <param name="types">The types to read.</param>
    /// <returns>One line per offending member.</returns>
    private static List<string> SecretMembers(IEnumerable<Type> types)
    {
        return types
            .SelectMany(type =>
            {
                return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property =>
                {
                    return (Type: type, Property: property);
                });
            })
            .Where(pair =>
            {
                return SecretWords.Any(word =>
                {
                    return pair.Property.Name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            })
            .Select(pair =>
            {
                return $"{pair.Type.Name}.{pair.Property.Name}";
            })
            .ToList();
    }

    /// <summary>Tells whether the needle appears in any of the texts.</summary>
    /// <param name="needle">The planted key.</param>
    /// <param name="haystacks">Everything that was searched.</param>
    /// <returns>True when at least one text holds it.</returns>
    private static bool Leaks(string needle, IEnumerable<string> haystacks)
    {
        return haystacks.Any(text =>
        {
            return text.Contains(needle, StringComparison.Ordinal);
        });
    }

    /// <summary>Reads the stored private key of the world's one zone.</summary>
    /// <param name="context">The world.</param>
    /// <returns>The plain scalar, which is the value every search below looks for.</returns>
    private static async Task<string> PlantedKeyAsync(DnsTestContext context)
    {
        using var reader = new DnsTestContext(new AdminCurrentUser(), context.DatabaseName);

        return (await reader.Database.ZoneSigningKeys.AsNoTracking().SingleAsync()).PrivateKey;
    }

    /// <summary>No DTO this module puts on the wire has a member named for a secret.</summary>
    [Fact]
    public void No_wire_type_has_a_member_named_for_a_secret()
    {
        Assert.Contains(typeof(ZoneDnssecDto), WireTypes);
        Assert.Contains(typeof(DsRecordDto), WireTypes);
        Assert.Contains(typeof(DnskeyRecordDto), WireTypes);
        Assert.Empty(SecretMembers(WireTypes));
    }

    /// <summary>Positive control: the same search flags a type that does carry such a member.</summary>
    [Fact]
    public void The_member_search_flags_a_type_that_carries_a_secret()
    {
        var found = SecretMembers([typeof(PlantedLeakyDto)]);

        Assert.Equal(["PlantedLeakyDto.PrivateKey"], found);
    }

    /// <summary>
    /// Every answer of the three commands and the query, serialised, and every zone read, holds no trace of the
    /// stored private key.
    /// </summary>
    [Fact]
    public async Task No_serialised_answer_holds_the_private_key()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        var answers = new List<object>();
        answers.Add((await context.EnableSigning.HandleAsync(
            new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None)).Value);
        answers.Add((await context.GetDnssec.HandleAsync(new GetZoneDnssecQuery(zoneId), CancellationToken.None)).Value);
        answers.Add((await context.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None)).Value);
        answers.Add((await context.WithdrawSigning.HandleAsync(
            new WithdrawZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None)).Value);
        context.SigningClock.Advance(TimeSpan.FromHours(48));
        var planted = await PlantedKeyAsync(context);
        answers.Add((await context.FinishSigningWithdrawal.HandleAsync(
            new FinishZoneSigningWithdrawalCommand(true, zoneId, "203.0.113.1", "tests"), CancellationToken.None)).Value);

        var texts = answers.Select(answer =>
        {
            return JsonSerializer.Serialize(answer, answer.GetType()) + answer;
        }).ToList();

        Assert.Equal(5, texts.Count);
        Assert.False(Leaks(planted, texts));

        // Positive control: the value searched for is the key that really went to the agent, and the same search
        // finds it in the text of the definition the agent received once that text is not masked.
        var sentKey = context.Agent.Puts.First(put =>
        {
            return put.Signing?.Key is not null;
        }).Signing!.Key!;
        Assert.Equal(planted, sentKey.PrivateKey.Reveal());
        Assert.True(Leaks(planted, [sentKey.PrivateKey.Reveal()]));

        // Vacuity guard: the answer that was searched does carry the DS record, so it had something to leak beside.
        Assert.Contains("Digest", texts[0], StringComparison.Ordinal);
    }

    /// <summary>The audit rows of a whole signing cycle, refused steps included, hold no trace of the key.</summary>
    [Fact]
    public async Task No_audit_row_holds_the_private_key_and_the_search_can_find_one_that_does()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;
        await context.EnableSigning.HandleAsync(new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var planted = await PlantedKeyAsync(context);
        await context.EnableSigning.HandleAsync(new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        await context.WithdrawSigning.HandleAsync(new WithdrawZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        await context.FinishSigningWithdrawal.HandleAsync(
            new FinishZoneSigningWithdrawalCommand(true, zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var rows = context.Entries.Select(entry =>
        {
            return JsonSerializer.Serialize(entry) + entry;
        }).ToList();

        Assert.True(rows.Count >= 4);
        Assert.False(Leaks(planted, rows));

        // Positive control: an entry that WAS given the key as its subject is found by the same search, which reads
        // each entry as JSON and as text, because JSON escapes the '+' of a base64 key and would hide it from a plain match.
        var planting = new RecordingAuditWriter();
        await new DnsAuditJournal(planting, new AdminCurrentUser()).RecordSuccessAsync(
            DnsAuditActions.ZoneSigningEnabled, planted, "203.0.113.1", "tests", CancellationToken.None);
        Assert.True(Leaks(planted, planting.Entries.Select(entry =>
        {
            return JsonSerializer.Serialize(entry) + entry;
        })));
    }

    /// <summary>
    /// The start-up pass over a signed zone logs no trace of the key, though it hands the key to the agent; the
    /// positive control is the definition the agent received, which does hold it.
    /// </summary>
    [Fact]
    public async Task The_reconciler_logs_no_trace_of_the_key_it_hands_to_the_agent()
    {
        var (admin, _) = await DnssecWorld.SigningAsync();
        using var _a = admin;
        var planted = await PlantedKeyAsync(admin);
        using var host = new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
        var logger = new RecordingLogger<DnsZoneReconciler>();
        using var scopes = new DnsReconcilerScopeFactory(host.Database, host.Agent);

        await new DnsZoneReconciler(scopes.Scopes, logger).ReconcileAsync(CancellationToken.None);

        var sent = Assert.Single(host.Agent.Puts);
        Assert.Equal(planted, sent.Signing!.Key!.PrivateKey.Reveal());
        Assert.NotEmpty(logger.Entries);
        Assert.False(Leaks(planted, logger.Entries.Select(entry =>
        {
            return entry.Message;
        })));
        Assert.DoesNotContain(planted, sent.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A zone the agent refuses to publish logs the refusal's code and never the key, and the failure carries a code only.</summary>
    [Fact]
    public async Task A_refused_publication_of_a_signed_zone_logs_no_trace_of_the_key()
    {
        var (admin, _) = await DnssecWorld.SigningAsync();
        using var _a = admin;
        var planted = await PlantedKeyAsync(admin);
        using var host = new DnsTestContext(new AnonymousCurrentUser(), admin.DatabaseName);
        var logger = new RecordingLogger<DnsZoneReconciler>();
        var refusing = new PutFailingDnsAgent(host.Agent, DnssecWorld.ZoneName);
        using var scopes = new DnsReconcilerScopeFactory(host.Database, refusing);

        var settled = await new DnsZoneReconciler(scopes.Scopes, logger).ReconcileAsync(CancellationToken.None);

        Assert.False(settled);
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Message.Contains(DnssecWorld.ZoneName, StringComparison.Ordinal);
        });
        Assert.False(Leaks(planted, logger.Entries.Select(entry =>
        {
            return entry.Message;
        })));
    }
}
