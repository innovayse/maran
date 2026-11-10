using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using WireDatabaseTransferStep = Maran.Agent.V1.DatabaseTransferStep;
using WireSourceDatabaseServer = Maran.Agent.V1.SourceDatabaseServer;
using WireSourceFailureReason = Maran.Agent.V1.SourceFailureReason;
using WireSourceInventoryStep = Maran.Agent.V1.SourceInventoryStep;
using WireSourceKind = Maran.Agent.V1.SourceKind;
using WireSourceSiteBackend = Maran.Agent.V1.SourceSiteBackend;
using WireSourceWarningKind = Maran.Agent.V1.SourceWarningKind;
using WireSourceWebServer = Maran.Agent.V1.SourceWebServer;
using WireTransferFailureReason = Maran.Agent.V1.TransferFailureReason;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Reads the wire messages of the source rpcs into the panel's own mirror.</summary>
/// <remarks>
/// <para>
/// The generated wire types may not leave this project, so the translation sits beside the client. It
/// reads a message once and states the three things the wire cannot say: "not determined" becomes the
/// absence of a value (so the panel's model never carries a zero it did not receive), a value this build
/// has no member for is refused, and a quantity that does not fit the panel's type is refused. A refusal is
/// a <c>null</c> return; the caller turns it into <c>AgentInvalidResponse</c> and never into a guess.
/// </para>
/// <para>
/// The mirror enums share their names with the wire's, so the wire's are aliased <c>Wire…</c> in this file
/// and a bare name always means the panel's.
/// </para>
/// </remarks>
internal static class TransferMapper
{
    /// <summary>Reads a freshly generated key pair.</summary>
    /// <param name="ok">The success payload of <c>GenerateSourceKey</c>.</param>
    /// <returns>The key pair, or <c>null</c> when it lacks either half or its fingerprint.</returns>
    public static AgentGeneratedSourceKey? ToGeneratedKey(GeneratedSourceKey ok)
    {
        if (string.IsNullOrEmpty(ok.PublicKey) || string.IsNullOrEmpty(ok.Fingerprint) || ok.PrivateKey.IsEmpty)
        {
            return null;
        }

        return new AgentGeneratedSourceKey(
            ok.PublicKey,
            new SensitiveString(ok.PrivateKey.ToStringUtf8()),
            ok.Fingerprint);
    }

    /// <summary>Reads the host keys a source offered.</summary>
    /// <param name="ok">The success payload of <c>ScanHostKey</c>.</param>
    /// <returns>
    /// The keys in the agent's order, or <c>null</c> when there is none (the administrator would have nothing
    /// to pin) or one lacks a member.
    /// </returns>
    public static IReadOnlyList<AgentScannedHostKey>? ToScannedKeys(ScannedHostKeys ok)
    {
        var keys = new List<AgentScannedHostKey>(ok.Keys.Count);

        foreach (var key in ok.Keys)
        {
            if (string.IsNullOrEmpty(key.Algorithm)
                || string.IsNullOrEmpty(key.Key)
                || string.IsNullOrEmpty(key.Fingerprint))
            {
                return null;
            }

            keys.Add(new AgentScannedHostKey(key.Algorithm, key.Key, key.Fingerprint));
        }

        return keys.Count == 0 ? null : keys;
    }

    /// <summary>Reads what the probes established about a source.</summary>
    /// <param name="ok">The success payload of <c>TestSource</c>.</param>
    /// <returns>
    /// The observation, or <c>null</c> when a value is one this build has no member for or a disk figure does
    /// not fit a signed 64-bit count.
    /// </returns>
    public static AgentTestedSource? ToTestedSource(TestedSource ok)
    {
        if (!TryKind(ok.Kind, out var kind)
            || !TryDatabaseServer(ok.DatabaseServer, out var databaseServer)
            || !TryWebServer(ok.WebServer, out var webServer))
        {
            return null;
        }

        long? free = null;
        long? total = null;

        if (ok.DiskKnown)
        {
            if (!TryLong(ok.HomeFreeBytes, out var freeBytes) || !TryLong(ok.HomeTotalBytes, out var totalBytes))
            {
                return null;
            }

            free = freeBytes;
            total = totalBytes;
        }

        return new AgentTestedSource(
            ok.IsRoot,
            free,
            total,
            NullIfEmpty(ok.SystemRelease),
            kind,
            kind == SourceKind.Maran ? NullIfEmpty(ok.MaranVersion) : null,
            databaseServer,
            webServer,
            ok.MailFound);
    }

    /// <summary>Reads one account found on a source.</summary>
    /// <param name="account">The wire account.</param>
    /// <returns>The account, or <c>null</c> when it has no name, a member is unreadable, or a figure does not fit.</returns>
    public static AgentSourceAccount? ToAccount(SourceAccount account)
    {
        if (string.IsNullOrEmpty(account.Name)
            || !TryOptionalBytes(account.DiskKnown, account.DiskBytes, out var diskBytes)
            || !TryInt(account.CronEntryCount, out var cronEntries)
            || !TryInt(account.MailboxCount, out var mailboxes))
        {
            return null;
        }

        var sites = new List<AgentSourceSite>(account.Sites.Count);
        foreach (var wireSite in account.Sites)
        {
            var site = ToSite(wireSite);
            if (site is null)
            {
                return null;
            }

            sites.Add(site);
        }

        var databases = new List<AgentSourceDatabase>(account.Databases.Count);
        foreach (var wireDatabase in account.Databases)
        {
            if (string.IsNullOrEmpty(wireDatabase.Name)
                || !TryOptionalBytes(wireDatabase.SizeKnown, wireDatabase.SizeBytes, out var sizeBytes))
            {
                return null;
            }

            databases.Add(new AgentSourceDatabase(wireDatabase.Name, sizeBytes));
        }

        return new AgentSourceAccount(account.Name, account.Home, diskBytes, sites, databases, cronEntries, mailboxes);
    }

    /// <summary>Reads one warning.</summary>
    /// <param name="warning">The wire warning.</param>
    /// <returns>The warning, or <c>null</c> for a kind this build has no member for (including "not a warning").</returns>
    public static AgentSourceWarning? ToWarning(SourceWarning warning)
    {
        var kind = warning.Kind switch
        {
            WireSourceWarningKind.NameRefused => (SourceWarningKind?)SourceWarningKind.NameRefused,
            WireSourceWarningKind.SiteUnreadable => SourceWarningKind.SiteUnreadable,
            WireSourceWarningKind.CronUnreadable => SourceWarningKind.CronUnreadable,
            WireSourceWarningKind.DatabasesUnreadable => SourceWarningKind.DatabasesUnreadable,
            WireSourceWarningKind.DiskUnreadable => SourceWarningKind.DiskUnreadable,
            WireSourceWarningKind.Truncated => SourceWarningKind.Truncated,
            WireSourceWarningKind.MailNotMigrated => SourceWarningKind.MailNotMigrated,
            _ => null,
        };

        return kind is null ? null : new AgentSourceWarning(kind.Value, warning.Subject);
    }

    /// <summary>Reads a step of the inventory read.</summary>
    /// <param name="step">The wire step.</param>
    /// <returns>The step, or <c>null</c> for a value this build has no member for (including "not a step").</returns>
    public static SourceInventoryStep? ToStep(WireSourceInventoryStep step)
    {
        return step switch
        {
            WireSourceInventoryStep.Accounts => SourceInventoryStep.Accounts,
            WireSourceInventoryStep.Sites => SourceInventoryStep.Sites,
            WireSourceInventoryStep.Databases => SourceInventoryStep.Databases,
            WireSourceInventoryStep.Cron => SourceInventoryStep.Cron,
            WireSourceInventoryStep.Disk => SourceInventoryStep.Disk,
            _ => null,
        };
    }

    /// <summary>Reads the reason a source call failed.</summary>
    /// <param name="reason">The wire reason.</param>
    /// <returns>
    /// The reason; a value this build has no member for is <see cref="SourceFailureReason.Unspecified"/>,
    /// because the error code beside it still states what happened and a newer agent's extra reason must not
    /// turn a failure into a refusal to read it.
    /// </returns>
    public static SourceFailureReason ToReason(WireSourceFailureReason reason)
    {
        return reason switch
        {
            WireSourceFailureReason.HostKeyChanged => SourceFailureReason.HostKeyChanged,
            WireSourceFailureReason.AuthRefused => SourceFailureReason.AuthRefused,
            WireSourceFailureReason.Unreachable => SourceFailureReason.Unreachable,
            WireSourceFailureReason.AddressRefused => SourceFailureReason.AddressRefused,
            _ => SourceFailureReason.Unspecified,
        };
    }

    /// <summary>Reads the reason a transfer failed.</summary>
    /// <param name="reason">The wire reason.</param>
    /// <returns>
    /// The reason, or <see cref="TransferFailureReason.Unspecified"/> for one this build has no member for,
    /// for the reason <see cref="ToReason"/> gives.
    /// </returns>
    public static TransferFailureReason ToTransferReason(WireTransferFailureReason reason)
    {
        return reason switch
        {
            WireTransferFailureReason.DiskInsufficient => TransferFailureReason.DiskInsufficient,
            WireTransferFailureReason.PullFailed => TransferFailureReason.PullFailed,
            WireTransferFailureReason.PullIncomplete => TransferFailureReason.PullIncomplete,
            WireTransferFailureReason.DumpFailed => TransferFailureReason.DumpFailed,
            WireTransferFailureReason.DumpTooLarge => TransferFailureReason.DumpTooLarge,
            WireTransferFailureReason.LoadRefused => TransferFailureReason.LoadRefused,
            WireTransferFailureReason.LoadFailed => TransferFailureReason.LoadFailed,
            _ => TransferFailureReason.Unspecified,
        };
    }

    /// <summary>Reads the step of a database transfer.</summary>
    /// <param name="step">The wire step.</param>
    /// <returns>The step, or <c>null</c> for a step this build has no member for.</returns>
    public static DatabaseTransferStep? ToDatabaseStep(WireDatabaseTransferStep step)
    {
        return step switch
        {
            WireDatabaseTransferStep.Dumping => DatabaseTransferStep.Dumping,
            WireDatabaseTransferStep.Loading => DatabaseTransferStep.Loading,
            _ => null,
        };
    }

    /// <summary>Reads one site.</summary>
    /// <param name="site">The wire site.</param>
    /// <returns>The site, or <c>null</c> when it has no domain or a backend this build has no member for.</returns>
    private static AgentSourceSite? ToSite(SourceSite site)
    {
        if (string.IsNullOrEmpty(site.Domain))
        {
            return null;
        }

        SourceSiteBackend backend;
        switch (site.Backend)
        {
            case WireSourceSiteBackend.Unspecified:
                backend = SourceSiteBackend.Unknown;
                break;
            case WireSourceSiteBackend.Static:
                backend = SourceSiteBackend.Static;
                break;
            case WireSourceSiteBackend.Php:
                backend = SourceSiteBackend.Php;
                break;
            case WireSourceSiteBackend.Proxy:
                backend = SourceSiteBackend.Proxy;
                break;
            default:
                return null;
        }

        return new AgentSourceSite(
            site.Domain,
            site.Aliases.ToList(),
            backend,
            backend == SourceSiteBackend.Php ? NullIfEmpty(site.PhpVersion) : null,
            site.DocumentRoot);
    }

    /// <summary>Reads the kind of host a source is.</summary>
    /// <param name="wire">The wire value.</param>
    /// <param name="kind">The kind, or <c>null</c> for "not determined".</param>
    /// <returns><c>false</c> for a value this build has no member for.</returns>
    private static bool TryKind(WireSourceKind wire, out SourceKind? kind)
    {
        switch (wire)
        {
            case WireSourceKind.Unspecified:
                kind = null;
                return true;
            case WireSourceKind.GenericLinux:
                kind = SourceKind.GenericLinux;
                return true;
            case WireSourceKind.Maran:
                kind = SourceKind.Maran;
                return true;
            default:
                kind = null;
                return false;
        }
    }

    /// <summary>Reads the database server found on a source.</summary>
    /// <param name="wire">The wire value.</param>
    /// <param name="server">The server, or <c>null</c> for "none found or the probe could not run".</param>
    /// <returns><c>false</c> for a value this build has no member for.</returns>
    private static bool TryDatabaseServer(WireSourceDatabaseServer wire, out SourceDatabaseServer? server)
    {
        switch (wire)
        {
            case WireSourceDatabaseServer.Unspecified:
                server = null;
                return true;
            case WireSourceDatabaseServer.Mariadb:
                server = SourceDatabaseServer.MariaDb;
                return true;
            case WireSourceDatabaseServer.Mysql:
                server = SourceDatabaseServer.MySql;
                return true;
            default:
                server = null;
                return false;
        }
    }

    /// <summary>Reads the web server found on a source.</summary>
    /// <param name="wire">The wire value.</param>
    /// <param name="server">The server, or <c>null</c> for "none found or the probe could not run".</param>
    /// <returns><c>false</c> for a value this build has no member for.</returns>
    private static bool TryWebServer(WireSourceWebServer wire, out SourceWebServer? server)
    {
        switch (wire)
        {
            case WireSourceWebServer.Unspecified:
                server = null;
                return true;
            case WireSourceWebServer.Nginx:
                server = SourceWebServer.Nginx;
                return true;
            case WireSourceWebServer.Apache:
                server = SourceWebServer.Apache;
                return true;
            default:
                server = null;
                return false;
        }
    }

    /// <summary>Reads a byte figure that is only meaningful beside its "known" flag.</summary>
    /// <param name="known">Whether the figure was measured.</param>
    /// <param name="bytes">The wire figure.</param>
    /// <param name="value">The figure, or <c>null</c> when it was not measured.</param>
    /// <returns><c>false</c> when it was measured and does not fit a signed 64-bit count.</returns>
    private static bool TryOptionalBytes(bool known, ulong bytes, out long? value)
    {
        value = null;

        if (!known)
        {
            return true;
        }

        if (!TryLong(bytes, out var measured))
        {
            return false;
        }

        value = measured;
        return true;
    }

    /// <summary>Narrows an unsigned 64-bit figure to the signed one the panel stores.</summary>
    /// <param name="value">The wire figure.</param>
    /// <param name="narrowed">The figure as a signed count.</param>
    /// <returns><c>false</c> when it does not fit.</returns>
    private static bool TryLong(ulong value, out long narrowed)
    {
        if (value > long.MaxValue)
        {
            narrowed = 0;
            return false;
        }

        narrowed = (long)value;
        return true;
    }

    /// <summary>Narrows an unsigned 32-bit count to the signed one the panel stores.</summary>
    /// <param name="value">The wire count.</param>
    /// <param name="narrowed">The count as a signed one.</param>
    /// <returns><c>false</c> when it does not fit.</returns>
    private static bool TryInt(uint value, out int narrowed)
    {
        if (value > int.MaxValue)
        {
            narrowed = 0;
            return false;
        }

        narrowed = (int)value;
        return true;
    }

    /// <summary>Reads the wire's empty text as the absence of a value.</summary>
    /// <param name="text">The wire text.</param>
    /// <returns>The text, or <c>null</c> when it is empty.</returns>
    private static string? NullIfEmpty(string text)
    {
        return text.Length == 0 ? null : text;
    }
}
