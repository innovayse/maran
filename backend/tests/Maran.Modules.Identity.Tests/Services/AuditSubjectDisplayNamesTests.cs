using System.Globalization;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Covers the operator-facing text of an audit entry's subject: the machine lines the chain verdict,
/// the retention window and run, the alert thresholds and the log level write are spelled as sentences
/// in the reader's language, and every subject that is not such a line is returned untouched.
/// </summary>
/// <remarks>
/// The pinned defect, measured in a browser in hy: the journal's subject column read
/// <c>intact rowsChecked=164</c>, <c>disk=90;cpu=off;memory=off -> disk=90;cpu=off;memory=off</c> and
/// <c>Warning -> Information</c> verbatim beside translated headers (issue #509). The resolver is the
/// ACCEPTING half of the same change: a name, an address and a line of an unknown shape come back as
/// they were stored, so the translation never swallows a subject it does not understand.
/// </remarks>
public sealed class AuditSubjectDisplayNamesTests
{
    /// <summary>The chain verdict is a sentence with the count, not a key=value line.</summary>
    [Fact]
    public void An_intact_chain_verdict_is_a_sentence_with_the_count()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () => { return names.Of(IdentityAuditActions.AuditChainVerified, "intact rowsChecked=164"); });

        Assert.Equal("Chain intact, 164 rows checked", text);
    }

    /// <summary>A broken verdict names the kind of break and where it began.</summary>
    [Fact]
    public void A_broken_chain_verdict_names_the_break_and_the_first_row()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () =>
        {
            return names.Of(IdentityAuditActions.AuditChainVerified, "broken break=IndexGap firstBrokenChainIndex=7 rowsChecked=40");
        });

        Assert.Equal("Chain broken (a row is missing) from row 7, 40 rows checked", text);
    }

    /// <summary>A break with no index is spelled without one.</summary>
    [Fact]
    public void A_broken_chain_verdict_without_an_index_is_spelled_without_one()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () =>
        {
            return names.Of(IdentityAuditActions.AuditChainVerified, "broken break=SaltMissing firstBrokenChainIndex= rowsChecked=3");
        });

        Assert.Equal("Chain broken (a row has neither a salt nor an erasure mark), 3 rows checked", text);
    }

    /// <summary>The same line reads in the reader's language: russian and armenian differ from english.</summary>
    /// <remarks>
    /// The axis that can go blind: a missing <c>.ru</c> or <c>.hy</c> entry falls back silently to the
    /// English text, so inequality against English is what a deleted translation cannot satisfy.
    /// </remarks>
    [Fact]
    public void The_chain_verdict_reads_in_russian_and_armenian()
    {
        var names = IdentityTestContext.SubjectNames();
        const string stored = "intact rowsChecked=164";

        Assert.Equal("Цепочка цела, проверено строк: 164", WithCulture("ru", () => { return names.Of(IdentityAuditActions.AuditChainVerified, stored); }));
        Assert.Equal("Շղթան անխախտ է, ստուգվել է 164 տող", WithCulture("hy", () => { return names.Of(IdentityAuditActions.AuditChainVerified, stored); }));
    }

    /// <summary>A retention window change spells both windows, and "no limit" is a phrase, not a code.</summary>
    [Fact]
    public void A_retention_change_spells_both_windows()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () => { return names.Of(IdentityAuditActions.AuditRetentionChanged, "from 90 to indefinite"); });

        Assert.Equal("From 90 days to no limit", text);
    }

    /// <summary>A finished retention run with no cutoff spells the window, the count and the index.</summary>
    [Fact]
    public void A_retention_run_without_a_cutoff_is_a_sentence()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () =>
        {
            return names.Of(
                IdentityAuditActions.AuditRetentionRun,
                "retentionDays=indefinite cutoff=none purged=0 throughChainIndex=0");
        });

        Assert.Equal("Window no limit, cutoff none, 0 rows removed, up to chain index 0", text);
    }

    /// <summary>A run with a cutoff instant carries no raw key=value text and no ISO timestamp.</summary>
    [Fact]
    public void A_retention_run_with_a_cutoff_shows_no_raw_fields()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () =>
        {
            return names.Of(
                IdentityAuditActions.AuditRetentionRun,
                "retentionDays=30 cutoff=2026-10-01T00:00:00.0000000Z purged=12 throughChainIndex=99");
        });

        Assert.DoesNotContain("=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("T00:00", text, StringComparison.Ordinal);
        Assert.StartsWith("Window 30 days, cutoff ", text, StringComparison.Ordinal);
        Assert.EndsWith("12 rows removed, up to chain index 99", text, StringComparison.Ordinal);
    }

    /// <summary>A run that could not finish says so.</summary>
    [Fact]
    public void A_failed_retention_run_says_it_failed()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () => { return names.Of(IdentityAuditActions.AuditRetentionRun, "retentionDays=30 failed"); });

        Assert.Equal("Window 30 days, the run failed", text);
    }

    /// <summary>The alert thresholds change names each figure and says which are not watched.</summary>
    [Fact]
    public void A_thresholds_change_names_each_figure()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("en", () =>
        {
            return names.Of(
                MonitoringAuditActions.AlertThresholdsSaved,
                "disk=90;cpu=off;memory=off -> disk=85;cpu=80;memory=off");
        });

        Assert.Equal(
            "Disk 90%, processor not watched, memory not watched → Disk 85%, processor 80%, memory not watched",
            text);
    }

    /// <summary>The thresholds change reads in the reader's language.</summary>
    [Fact]
    public void A_thresholds_change_reads_in_russian()
    {
        var names = IdentityTestContext.SubjectNames();

        var text = WithCulture("ru", () =>
        {
            return names.Of(MonitoringAuditActions.AlertThresholdsSaved, "disk=90;cpu=off;memory=off -> disk=90;cpu=80;memory=off");
        });

        Assert.Equal("Диск 90%, процессор не отслеживается, память не отслеживается → Диск 90%, процессор 80%, память не отслеживается", text);
    }

    /// <summary>A log level change names both levels in the reader's language, not the English enum names.</summary>
    [Fact]
    public void A_log_level_change_names_both_levels_in_the_readers_language()
    {
        var names = IdentityTestContext.SubjectNames();

        Assert.Equal("Warning → Information", WithCulture("en", () => { return names.Of(MonitoringAuditActions.PanelLogLevelChanged, "Warning -> Information"); }));
        Assert.Equal("Предупреждение → Информация", WithCulture("ru", () => { return names.Of(MonitoringAuditActions.PanelLogLevelChanged, "Warning -> Information"); }));
        Assert.Equal("Նախազգուշացում → Տեղեկատվություն", WithCulture("hy", () => { return names.Of(MonitoringAuditActions.PanelLogLevelChanged, "Warning -> Information"); }));
    }

    /// <summary>A subject of an action that is not read is returned exactly as stored.</summary>
    /// <remarks>The accepting control: the resolver does not touch a login name, an address or an id.</remarks>
    [Theory]
    [InlineData(IdentityAuditActions.LoginSucceeded, "edgar")]
    [InlineData(IdentityAuditActions.LoggedOut, "edgar")]
    [InlineData(IdentityAuditActions.SessionRevoked, "8656fbcd-df1e-4f75-958c-4068e07e886b")]
    [InlineData("SomeMarketplaceModuleAction", "key=value")]
    public void A_subject_that_is_not_a_machine_line_is_returned_as_stored(string action, string subject)
    {
        var names = IdentityTestContext.SubjectNames();

        Assert.Equal(subject, WithCulture("hy", () => { return names.Of(action, subject); }));
    }

    /// <summary>A read action whose subject is not in the shape its producer writes is returned as stored.</summary>
    /// <remarks>An older release's line, or a hand-written one, is shown rather than half-parsed.</remarks>
    [Theory]
    [InlineData(IdentityAuditActions.AuditChainVerified, "something else entirely")]
    [InlineData(IdentityAuditActions.AuditChainVerified, "intact rowsChecked=many")]
    [InlineData(IdentityAuditActions.AuditRetentionChanged, "from 90")]
    [InlineData(IdentityAuditActions.AuditRetentionRun, "retentionDays=soon cutoff=none purged=0 throughChainIndex=0")]
    [InlineData(MonitoringAuditActions.AlertThresholdsSaved, "disk=90 -> disk=85")]
    [InlineData(MonitoringAuditActions.PanelLogLevelChanged, "Loud -> Quiet")]
    public void A_read_action_with_an_unknown_shape_is_returned_as_stored(string action, string subject)
    {
        var names = IdentityTestContext.SubjectNames();

        Assert.Equal(subject, WithCulture("en", () => { return names.Of(action, subject); }));
    }

    /// <summary>Runs one probe under a fixed culture and restores the previous one.</summary>
    /// <typeparam name="T">The probe's answer type.</typeparam>
    /// <param name="culture">The culture name to resolve resources and format values for.</param>
    /// <param name="probe">The lookup to run under it.</param>
    /// <returns>The probe's answer.</returns>
    private static T WithCulture<T>(string culture, Func<T> probe)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return probe();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }
}
