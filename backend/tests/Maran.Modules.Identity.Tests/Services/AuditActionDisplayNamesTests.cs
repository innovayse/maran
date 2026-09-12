using System.Globalization;
using System.Reflection;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Covers the operator-facing name of an audit action, and — the part that makes this a fix rather
/// than a handful of translations — that EVERY action the panel can record has one, in every
/// language the panel speaks.
/// </summary>
/// <remarks>
/// <para>
/// The pinned defect, measured in a browser: the audit screen rendered <c>BackupRestored</c>,
/// <c>AccountSuspended</c> and <c>AdministratorCreated</c> verbatim in the middle of a localized
/// Russian screen. Naming the actions that happened to be on screen would not have been a fix,
/// because an action with no entry falls back to the raw constant and IS the defect — so the
/// closed set the actions come from is walked here rather than sampled.
/// </para>
/// <para>
/// <b>The closed set is <see cref="AuditActions"/> itself.</b> Every shipped producer records
/// through a module journal, and every journal passes one of these constants (`maran structure`
/// keeps entry construction inside the journals); an action outside the set can only come from a
/// marketplace module this assembly never compiled against, which is exactly what the fallback is
/// for and what the fallback test pins.
/// </para>
/// </remarks>
public sealed class AuditActionDisplayNamesTests
{
    /// <summary>Every constant of <see cref="AuditActions"/> — the closed set the walks cover.</summary>
    private static readonly List<string> ClosedSet = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field =>
        {
            return field.IsLiteral && field.FieldType == typeof(string);
        })
        .Select(field =>
        {
            return (string)field.GetRawConstantValue()!;
        })
        .ToList();

    /// <summary>Every action the panel can record has an english name an operator can read.</summary>
    /// <remarks>
    /// The resolver answers the raw action for a missing entry, so equality IS the miss. The walk
    /// is over the closed set, never over a list of what a screen happened to show.
    /// </remarks>
    [Fact]
    public void Every_audit_action_has_an_english_name_an_operator_can_read()
    {
        var names = IdentityTestContext.ActionNames();
        var unnamed = WithCulture("en", () =>
        {
            return ClosedSet.Where(action =>
            {
                return string.Equals(names.Of(action), action, StringComparison.Ordinal);
            }).ToList();
        });

        Assert.Empty(unnamed);
    }

    /// <summary>Every audit action is named differently in russian than in english.</summary>
    /// <remarks>
    /// The axis that can go blind: a missing <c>.ru</c> entry does NOT report ResourceNotFound —
    /// the localizer silently falls back to the neutral English text — so "a name came back" would
    /// pass with the whole Russian file deleted. Inequality against the English value is what a
    /// silently-missing translation cannot satisfy.
    /// </remarks>
    [Fact]
    public void Every_audit_action_is_named_in_russian_not_in_english()
    {
        var names = IdentityTestContext.ActionNames();
        var untranslated = ClosedSet
            .Where(action =>
            {
                var english = WithCulture("en", () => { return names.Of(action); });
                var russian = WithCulture("ru", () => { return names.Of(action); });
                return string.Equals(english, russian, StringComparison.Ordinal);
            })
            .ToList();

        Assert.Empty(untranslated);
    }

    /// <summary>Every audit action is named differently in armenian than in english.</summary>
    /// <remarks>Same blind axis as the Russian walk: a missing <c>.hy</c> entry falls back silently.</remarks>
    [Fact]
    public void Every_audit_action_is_named_in_armenian_not_in_english()
    {
        var names = IdentityTestContext.ActionNames();
        var untranslated = ClosedSet
            .Where(action =>
            {
                var english = WithCulture("en", () => { return names.Of(action); });
                var armenian = WithCulture("hy", () => { return names.Of(action); });
                return string.Equals(english, armenian, StringComparison.Ordinal);
            })
            .ToList();

        Assert.Empty(untranslated);
    }

    /// <summary>The closed set the walks cover was actually enumerated.</summary>
    /// <remarks>
    /// Vacuity guard on the axis that can go blind (rules/testing.md): reflection with the wrong
    /// binding flags returns an empty list, and every walk above then passes over nothing. Two
    /// known members prove the enumeration read the real type, and the floor is the set's size on
    /// the day this suite was written — the set only grows, because renaming a recorded action
    /// orphans every row already journalled under it.
    /// </remarks>
    [Fact]
    public void The_closed_set_of_audit_actions_was_actually_enumerated()
    {
        Assert.Contains(AuditActions.LoginSucceeded, ClosedSet);
        Assert.Contains(AuditActions.BackupRestored, ClosedSet);
        Assert.True(ClosedSet.Count >= 61, $"only {ClosedSet.Count} actions enumerated");
    }

    /// <summary>The english name is the sentence, stated exactly, not merely some string.</summary>
    /// <remarks>
    /// A test asserting "a name came back" would pass against a screen full of raw constants,
    /// which is the defect. The VALUE is asserted (rules/testing.md).
    /// </remarks>
    [Fact]
    public void The_english_name_of_a_restore_is_the_sentence_it_should_be()
    {
        var names = IdentityTestContext.ActionNames();

        var name = WithCulture("en", () => { return names.Of(AuditActions.BackupRestored); });

        Assert.Equal("Account restored from a backup", name);
    }

    /// <summary>In russian the three actions measured raw on the live screen are russian sentences.</summary>
    /// <remarks>
    /// The exact rows from the live run: <c>BackupRestored</c>, <c>AccountSuspended</c> and
    /// <c>AdministratorCreated</c>, rendered verbatim beside translated column headers. The
    /// assertions are on the Russian VALUES, so a build that resolved the neutral English text —
    /// the state a missing <c>.ru</c> entry silently produces — fails here.
    /// </remarks>
    [Theory]
    [InlineData(AuditActions.BackupRestored, "Аккаунт восстановлен из резервной копии")]
    [InlineData(AuditActions.AccountSuspended, "Аккаунт приостановлен")]
    [InlineData(AuditActions.AdministratorCreated, "Создан администратор")]
    public void The_russian_name_of_a_measured_action_is_the_russian_sentence(string action, string expected)
    {
        var names = IdentityTestContext.ActionNames();

        var name = WithCulture("ru", () => { return names.Of(action); });

        Assert.Equal(expected, name);
    }

    /// <summary>In armenian a measured action is the armenian sentence.</summary>
    [Fact]
    public void The_armenian_name_of_a_measured_action_is_the_armenian_sentence()
    {
        var names = IdentityTestContext.ActionNames();

        var name = WithCulture("hy", () => { return names.Of(AuditActions.AccountSuspended); });

        Assert.Equal("Հաշիվը կասեցվեց", name);
    }

    /// <summary>An action this build has never heard of falls back to itself rather than to a key.</summary>
    /// <remarks>
    /// The accepting control for the walks above, and the marketplace contract: the fallback is
    /// deliberate, and what must NOT happen is the resx key leaking out — <c>AuditActionWhatever</c>
    /// is worse than the constant, because it names a file nobody outside this repository can read.
    /// </remarks>
    [Fact]
    public void An_action_this_build_has_never_heard_of_falls_back_to_itself()
    {
        var names = IdentityTestContext.ActionNames();

        Assert.Equal("SomeMarketplaceModuleAction", names.Of("SomeMarketplaceModuleAction"));
    }

    /// <summary>Runs one probe under a fixed UI culture and restores the previous one.</summary>
    /// <typeparam name="T">The probe's answer type.</typeparam>
    /// <param name="culture">The culture name to resolve resources for.</param>
    /// <param name="probe">The lookup to run under it.</param>
    /// <returns>The probe's answer.</returns>
    private static T WithCulture<T>(string culture, Func<T> probe)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return probe();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
