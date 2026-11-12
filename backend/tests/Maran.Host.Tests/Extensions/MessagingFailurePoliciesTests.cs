using Maran.Host.Extensions;
using Maran.Modules.Sites.Services;
using Wolverine;
using Wolverine.ErrorHandling;

namespace Maran.Host.Tests.Extensions;

/// <summary>
/// The bus is told how to retry a message whose handler said "a site is busy". Left to the library's
/// default, the retry would be immediate and would queue behind the very operation that held the site.
/// </summary>
public sealed class MessagingFailurePoliciesTests
{
    /// <summary>A site-busy failure has a rule of its own.</summary>
    [Fact]
    public void A_site_busy_failure_has_a_retry_rule()
    {
        var options = new WolverineOptions();

        MessagingFailurePolicies.Apply(options);

        Assert.Contains(((IWithFailurePolicies)options).Failures, rule =>
        {
            return rule.Match.Matches(new SiteBusyException("a.example stayed busy"));
        });
    }

    /// <summary>The control: the rule is for that failure and not for every failure.</summary>
    [Fact]
    public void The_rule_does_not_catch_other_failures()
    {
        var options = new WolverineOptions();

        MessagingFailurePolicies.Apply(options);

        Assert.DoesNotContain(((IWithFailurePolicies)options).Failures, rule =>
        {
            return rule.Match.Matches(new InvalidOperationException("not a busy site"));
        });
    }

    /// <summary>
    /// The rule applies the delays the policy states, one per attempt: the first failure is retried after the first
    /// delay, the second after the second, and so on, each as a scheduled (not immediate) retry.
    /// </summary>
    /// <remarks>
    /// Read from the rule the bus was configured with, not from the constant, so a rule built with other delays,
    /// or with an immediate retry, makes this fail.
    /// </remarks>
    [Fact]
    public void The_rule_schedules_each_attempt_at_the_stated_delay()
    {
        var options = new WolverineOptions();
        MessagingFailurePolicies.Apply(options);
        var rule = ((IWithFailurePolicies)options).Failures
            .Single(candidate => { return candidate.Match.Matches(new SiteBusyException("a.example stayed busy")); });

        var failure = new SiteBusyException("a.example stayed busy");
        var scheduled = new List<string>();
        foreach (var slot in rule)
        {
            // The bus builds the answer for attempt N from the slot of attempt N; its description is the
            // delay it will schedule the retry after.
            Assert.True(rule.TryCreateContinuation(failure, new Envelope { Attempts = slot.Attempt }, out var continuation));
            scheduled.Add(continuation.ToString()!);
        }

        Assert.Equal(
            MessagingFailurePolicies.SiteBusyRetryDelays.Select(delay => { return $"Schedule Retry in {(int)delay.TotalSeconds} seconds"; }).ToList(),
            scheduled);
    }

    /// <summary>The retries are spaced out and each later than the one before.</summary>
    [Fact]
    public void The_retries_are_spaced_out_and_grow()
    {
        var delays = MessagingFailurePolicies.SiteBusyRetryDelays;

        Assert.NotEmpty(delays);
        Assert.All(delays, delay =>
        {
            Assert.True(delay >= TimeSpan.FromSeconds(30));
        });
        Assert.Equal(delays.Order().ToList(), delays.ToList());
    }
}
