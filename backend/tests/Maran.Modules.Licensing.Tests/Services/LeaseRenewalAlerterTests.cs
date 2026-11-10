using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LeaseRenewalAlerter"/>: the alert is a licensing alert in the operator's language with the
/// count and one closed reason in it, never a resource key in place of a sentence, and the resolution says it is
/// over. The shared path (journal, mail, webhook event, feed entry) is the real one.
/// </summary>
public sealed class LeaseRenewalAlerterTests
{
    /// <summary>Builds the alerter over the real shared path, recording what it publishes.</summary>
    /// <param name="bus">Where the outputs are published.</param>
    /// <returns>The alerter.</returns>
    private static LeaseRenewalAlerter Alerter(RecordingMessageBus bus)
    {
        return new LeaseRenewalAlerter(
            new OperatorAlertDispatcher(
                StubAlertRecipientDirectory.Of([RenewalWorld.OperatorAddress]),
                bus,
                new FixedClock(DateTimeOffset.UnixEpoch)),
            new LicensingAuditJournal(new RecordingAuditWriter(), new FakeCurrentUser(isAdmin: true)),
            RenewalWorld.AlertText());
    }

    /// <summary>The text lookup returns the key when a key is missing, so the assertions below can see a missing entry.</summary>
    [Fact]
    public void A_missing_resource_entry_reads_as_its_key_so_the_checks_below_can_observe_it()
    {
        Assert.Equal("NoSuchKey", RenewalWorld.AlertText()["NoSuchKey"].Value);
    }

    /// <summary>Every failure reason has a sentence in every language: neither the subject nor the body is a resource key.</summary>
    [Theory]
    [InlineData(LeaseRenewalOutcome.Unreachable)]
    [InlineData(LeaseRenewalOutcome.Refused)]
    [InlineData(LeaseRenewalOutcome.AnswerRejected)]
    [InlineData(LeaseRenewalOutcome.NotNewer)]
    [InlineData(LeaseRenewalOutcome.WriteFailed)]
    public async Task Every_failure_reason_reads_as_a_sentence_in_every_language(LeaseRenewalOutcome outcome)
    {
        foreach (var culture in new[] { "en", "ru", "hy" })
        {
            using var scope = new CultureScope(culture);
            var bus = new RecordingMessageBus();

            await Alerter(bus).RaiseAsync(outcome, 3, CancellationToken.None);

            var mail = Assert.Single(bus.Published.OfType<SendMailRequested>());
            Assert.DoesNotContain("LeaseRenewal", mail.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain("LeaseRenewal", mail.Body, StringComparison.Ordinal);
            Assert.Contains("3", mail.Body, StringComparison.Ordinal);
        }
    }

    /// <summary>The Russian and Armenian texts are translations, not the English sentence repeated.</summary>
    [Fact]
    public async Task The_russian_and_armenian_subjects_differ_from_the_english_one()
    {
        var subjects = new Dictionary<string, string>();
        foreach (var culture in new[] { "en", "ru", "hy" })
        {
            using var scope = new CultureScope(culture);
            var bus = new RecordingMessageBus();

            await Alerter(bus).RaiseAsync(LeaseRenewalOutcome.Unreachable, 3, CancellationToken.None);

            subjects[culture] = Assert.Single(bus.Published.OfType<SendMailRequested>()).Subject;
        }

        Assert.NotEqual(subjects["en"], subjects["ru"]);
        Assert.NotEqual(subjects["en"], subjects["hy"]);
    }

    /// <summary>The resolution is a resolved licensing alert with its own sentence and no count.</summary>
    [Fact]
    public async Task The_resolution_is_a_resolved_licensing_alert_with_its_own_sentence()
    {
        var bus = new RecordingMessageBus();

        await Alerter(bus).ResolveAsync(CancellationToken.None);

        var alert = Assert.Single(bus.Published.OfType<OperatorAlertRaised>()).Alert;
        Assert.Equal(AlertSource.Licensing, alert.Source);
        Assert.Equal(AlertTransition.Resolved, alert.Transition);
        var mail = Assert.Single(bus.Published.OfType<SendMailRequested>());
        Assert.DoesNotContain("LeaseRenewal", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaseRenewal", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>The alert carries no licence id, address or path: its detail is the outcome word and a count.</summary>
    [Fact]
    public async Task The_alert_detail_is_the_outcome_word_and_the_count_only()
    {
        var bus = new RecordingMessageBus();

        await Alerter(bus).RaiseAsync(LeaseRenewalOutcome.Refused, 5, CancellationToken.None);

        var alert = Assert.Single(bus.Published.OfType<OperatorAlertRaised>()).Alert;
        Assert.Equal("outcome=Refused consecutiveFailures=5", alert.Detail);
        Assert.Equal("licence lease", alert.Subject);
    }
}
