namespace Maran.ArchitectureTests;

/// <summary>
/// Every audit action this build can record is named for an operator in english, russian and
/// armenian — whichever module declared it, and wherever that module chose to keep the constant.
/// </summary>
/// <remarks>
/// <para>
/// The audit trail is the record an operator consults when something has gone wrong, and a customer
/// may read it too. <c>AuditActionDisplayNames.Of</c> answers a missing entry with the raw action,
/// so a gap does not fail, does not warn, and does not look like a defect from the backend at all:
/// it prints <c>FtpUserCreated</c> in the middle of a Russian screen.
/// </para>
/// <para>
/// <b>Why this suite exists beside <c>AuditActionDisplayNamesTests</c> and does not replace it.</b>
/// That suite exercises the RESOLVER — the fallback, the culture lookup, the exact sentences three
/// measured defects produced — and it is right where it is, in the module that owns the resolver.
/// What it cannot do from there is enumerate the actions other modules declare: its walk is over
/// <c>AuditActions</c>, and six actions the panel writes every day are not in it. Enumerating across
/// modules is an architecture question and this is where the suites that ask them live
/// (<see cref="AuditActionVocabulary"/> carries the enumeration and its blind spots).
/// </para>
/// <para>
/// UNOBSERVED HERE: whether a translation is CORRECT. This suite sees that a Russian entry exists
/// and is not the English text; a fluent Russian sentence saying the wrong thing passes it. Nothing
/// in this repository can observe that, and the honest place to say so is here rather than in a
/// report nobody reads twice.
/// </para>
/// </remarks>
public sealed class AuditActionDisplayNameTests
{
    /// <summary>The resource family <c>AuditActionDisplayNames</c> resolves against.</summary>
    /// <remarks>
    /// Named exactly, not searched for: the resolver takes <c>IStringLocalizer&lt;DisplayNames&gt;</c>
    /// of the Identity module, so an <c>AuditAction*</c> key sitting in some OTHER module's resource
    /// file would satisfy a repository-wide key search and still leave the screen printing the raw
    /// action. A gate observes the artefact the system actually reads (rules/testing.md).
    /// </remarks>
    private const string DisplayNameFamily = "Maran.Modules/Identity/Resources/DisplayNames.resx";

    /// <summary>
    /// The public string constants on a journal that are NOT actions, each with the reason, so that
    /// everything else is an action by default and a new one cannot be quietly excluded.
    /// </summary>
    /// <remarks>
    /// The default matters more than the entries. A guard that recognised actions by a naming
    /// convention — a constant whose value equals its own name, say — would silently stop seeing the
    /// first action somebody spells differently, and the direction of that failure is a clean green
    /// report. So every public constant on a journal is an action unless it is written down here.
    /// The staleness guard below refuses an entry that no longer names a real member, which is what
    /// keeps this list from rotting into a place a real action hides.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NotActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maran.Modules.Monitoring.Services.MonitoringAuditJournal.ModuleName"] =
                "The actor name of the module's unattended entries, not an action.",
            ["Maran.Modules.Identity.Services.IdentityAuditJournal.ModuleName"] =
                "The actor name of the module's unattended entries, not an action.",
            ["Maran.Modules.Ssl.Services.CertificateAuditJournal.ModuleName"] =
                "The actor name of the module's unattended entries, not an action.",
            ["Maran.Modules.Backups.Services.BackupAuditJournal.ModuleName"] =
                "The actor name of the module's unattended entries, not an action.",
            ["Maran.Modules.Notifications.Services.NotificationsAuditJournal.ModuleName"] =
                "The actor name of the module's unattended entries, not an action.",
            ["Maran.Modules.Firewall.Services.FirewallAuditJournal.SystemActor"] =
                "The actor recorded for an automatic ban, not an action.",
        };

    /// <summary>Every audit action any journal can record has a name in every language.</summary>
    [Fact]
    public void Every_audit_action_any_journal_can_record_has_a_name_in_every_language()
    {
        var neutralPath = ResourceTriples.NeutralPathOf(DisplayNameFamily);
        var neutral = ResourceTriples.Read(neutralPath);
        Assert.NotEmpty(neutral);

        var problems = new List<string>();
        foreach (var action in Vocabulary())
        {
            var key = AuditActionVocabulary.KeyPrefix + action;
            if (!neutral.TryGetValue(key, out var english) || string.IsNullOrWhiteSpace(english))
            {
                problems.Add(
                    $"'{action}' has no '{key}' entry in {DisplayNameFamily}, so the audit screen "
                    + "prints the machine action itself, in every language.");
                continue;
            }

            foreach (var culture in ResourceTriples.TranslatedCultures)
            {
                var translated = ResourceTriples.Read(ResourceTriples.TranslatedPathOf(neutralPath, culture));
                if (!translated.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"'{key}' has no {culture} entry; the screen falls back to '{english}'.");
                    continue;
                }

                if (string.Equals(value, english, StringComparison.Ordinal))
                {
                    problems.Add($"'{key}' reads '{english}' in {culture} as well as in english — it is untranslated.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "An audit action reaches the operator's screen as a machine identifier "
            + "(rules/architecture.md, 'The backend owns the data, the SPA renders it'):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    /// <summary>The vocabulary reads the journals and the shared constants it claims to enumerate.</summary>
    /// <remarks>
    /// The axis that can go blind (rules/testing.md): reflection over an assembly set that failed to
    /// load, or a renamed journal suffix, returns an empty enumeration and the walk above then
    /// passes over nothing — loudest at the moment it stopped looking. The positive control is the
    /// defect this suite was written for: <c>FtpAuditJournal.FtpUserCreated</c> is declared on a
    /// module journal and is in no shared list, so an enumeration that does not contain it is
    /// exactly the blind one that reported full coverage while the screen printed the constant.
    /// </remarks>
    [Fact]
    public void The_vocabulary_reads_the_journals_and_the_shared_constants_it_claims_to_enumerate()
    {
        var journals = AuditActionVocabulary.Journals()
            .Select(journal =>
            {
                return journal.FullName;
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Maran.Modules.Ftp.Services.FtpAuditJournal", journals);
        Assert.Contains("Maran.Modules.Sftp.Services.SftpAuditJournal", journals);
        Assert.Contains("Maran.Modules.Accounts.Services.AccountAuditJournal", journals);
        Assert.True(
            journals.Count >= 12,
            $"Only {journals.Count} audit journals were found; every module that writes entries has "
            + "one, so the assembly scan is partial and this suite is reporting on a fraction.");

        Assert.Contains("FtpUserCreated", Vocabulary());
        Assert.Contains("BackupRestored", Vocabulary());
        Assert.True(
            AuditActionVocabulary.SharedActions().Count >= 61,
            "The Sdk's shared action constants were not read; the enumeration is partial.");
    }

    /// <summary>Every excused journal constant still names a member that exists.</summary>
    /// <remarks>
    /// An exemption for a member that has been renamed or removed is worse than none: it is a hole
    /// in the default, sitting under a reason that reads as considered. The same staleness guard
    /// <see cref="AccountCascadeTests"/> keeps over its own register.
    /// </remarks>
    [Fact]
    public void Every_excused_journal_constant_still_names_a_member_that_exists()
    {
        var declared = AuditActionVocabulary.JournalConstants();

        var stale = NotActions.Keys.Where(member =>
        {
            return !declared.ContainsKey(member);
        }).ToList();

        Assert.True(
            stale.Count == 0,
            "These journal constants are excused from needing a display name and no longer exist, so "
            + "the excuse is now a place an action could hide: "
            + string.Join(", ", stale));
    }

    /// <summary>A missing entry is what this suite reports, and a present one is what it accepts.</summary>
    /// <remarks>
    /// The inverse control (rules/testing.md): a refusing gate is fed something it must ACCEPT and
    /// something it must REFUSE, against the same resource family the walk reads. Without this pair
    /// a lookup that answered "missing" for everything, or "present" for everything, would look the
    /// same as a working one from the walk's green line.
    /// </remarks>
    [Fact]
    public void A_missing_entry_is_what_this_suite_reports_and_a_present_one_is_what_it_accepts()
    {
        var neutral = ResourceTriples.Read(ResourceTriples.NeutralPathOf(DisplayNameFamily));

        Assert.True(neutral.ContainsKey(AuditActionVocabulary.KeyPrefix + "FtpUserCreated"));
        Assert.False(neutral.ContainsKey(AuditActionVocabulary.KeyPrefix + "SomeMarketplaceModuleAction"));
    }

    /// <summary>Every action this build can record, from the Sdk and from every module journal.</summary>
    /// <returns>The action values, ordered, with the declared non-actions removed.</returns>
    private static List<string> Vocabulary()
    {
        var fromJournals = AuditActionVocabulary.JournalConstants()
            .Where(constant =>
            {
                return !NotActions.ContainsKey(constant.Key);
            })
            .Select(constant =>
            {
                return constant.Value;
            });

        return AuditActionVocabulary.SharedActions()
            .Concat(fromJournals)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
