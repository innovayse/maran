using System.Globalization;
using System.Text;
using FluentValidation;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Resources;

namespace Maran.Modules.Cron.Validators;

/// <summary>
/// Validates the five fields of a <see cref="CronScheduleDto"/> before a command carrying one
/// reaches its handler. Shared by the create and the update validators through
/// <c>SetValidator</c>, so the two cannot drift into accepting different schedules.
/// </summary>
/// <remarks>
/// <para>
/// A mirror of the agent's own <c>CronSchedule</c>, and only a mirror: the agent re-validates every
/// field and its answer decides what is written to the crontab (rules/architecture.md "Agent").
/// Checking here is what lets a customer be told which field they got wrong, in their own language,
/// instead of being handed a refusal from a process they cannot see.
/// </para>
/// <para>
/// The grammar is exactly the agent's: a field is a comma-separated list of items, and an item is
/// one of <c>*</c>, <c>*/step</c>, <c>N</c>, <c>N-M</c> or <c>N-M/step</c>. Numbers are decimal,
/// unpadded and bounded per field; a range may not run backwards; a step is at least 1, at most the
/// distance between the field's bounds, and may follow only <c>*</c> or a range — a step needs a
/// span to step across, and <c>5/2</c> means different things to different crons.
/// </para>
/// <para>
/// Deliberately absent, because the agent refuses them too: month and weekday names, <c>@hourly</c>
/// and its relatives, and whitespace of any kind. Written as explicit character checks rather than
/// one regular expression, because the grammar is nested — a range inside an item inside a
/// comma-separated field — and a pattern that expressed all of it would be unreadable at exactly the
/// place a reader most needs to check it against the agent.
/// </para>
/// <para>
/// Every message is a bare resx key rather than an English sentence: the Host forwards a validation
/// message only when it is entirely alphanumeric and then resolves it as an error code against this
/// module's resources, so a sentence would be discarded and the customer would get the generic
/// failure instead.
/// </para>
/// </remarks>
public sealed class CronScheduleValidator : AbstractValidator<CronScheduleDto>
{
    /// <summary>The most bytes one field may be, matching the agent's ceiling.</summary>
    /// <remarks>
    /// Set above the longest field a caller can need — a fully enumerated minute field,
    /// <c>0,1,2,…,59</c>, is 169 characters — so nothing a crontab can express is refused by length.
    /// Counted in UTF-8 bytes because the agent counts the bytes it writes.
    /// </remarks>
    private const int MaximumFieldLengthInBytes = 256;

    /// <summary>The most digits any single number in a field may have.</summary>
    /// <remarks>
    /// The largest value any field accepts is 59, so three digits is already one more than a legal
    /// schedule needs. The cap is also what keeps the parse below <c>999</c> and therefore free of
    /// any overflow question.
    /// </remarks>
    private const int MaximumNumberDigits = 3;

    /// <summary>The item that means "every value this field has".</summary>
    private const string Wildcard = "*";

    /// <summary>Configures the field rules for <see cref="CronScheduleDto"/>.</summary>
    public CronScheduleValidator()
    {
        RuleFor(schedule => schedule.Minute)
            .Must(field =>
            {
                return IsValidField(field, minimum: 0, maximum: 59);
            })
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));

        RuleFor(schedule => schedule.Hour)
            .Must(field =>
            {
                return IsValidField(field, minimum: 0, maximum: 23);
            })
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));

        // One-based, unlike the three fields around it. The bounds are stated per field rather than
        // shared, because a step is capped at the SPAN between them: reading day-of-month's cap off
        // a zero-based field would let `*/31` through, and `*/31` selects the first of the month and
        // nothing else while looking like "every 31 days".
        RuleFor(schedule => schedule.DayOfMonth)
            .Must(field =>
            {
                return IsValidField(field, minimum: 1, maximum: 31);
            })
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));

        RuleFor(schedule => schedule.Month)
            .Must(field =>
            {
                return IsValidField(field, minimum: 1, maximum: 12);
            })
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));

        // Capped at 6 rather than at the 7 some crons also accept for Sunday: the agent renders what
        // it validated, and two spellings of one day is a value whose meaning depends on which cron
        // the host ships.
        RuleFor(schedule => schedule.DayOfWeek)
            .Must(field =>
            {
                return IsValidField(field, minimum: 0, maximum: 6);
            })
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));
    }

    /// <summary>Whether one whole field is acceptable.</summary>
    /// <param name="candidate">The field as the customer typed it.</param>
    /// <param name="minimum">The smallest number this field accepts.</param>
    /// <param name="maximum">The largest number this field accepts.</param>
    /// <returns>True when every comma-separated item of the field is acceptable.</returns>
    /// <remarks>
    /// The character-class refusals come first and by name, so that loosening the grammar below can
    /// never quietly re-admit a newline.
    /// </remarks>
    private static bool IsValidField(string? candidate, int minimum, int maximum)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(candidate) > MaximumFieldLengthInBytes)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        foreach (var item in candidate.Split(','))
        {
            if (!IsValidItem(item, minimum, maximum))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one comma-separated item of a field is acceptable.</summary>
    /// <param name="item">The item, which may carry a range and a step.</param>
    /// <param name="minimum">The smallest number this field accepts.</param>
    /// <param name="maximum">The largest number this field accepts.</param>
    /// <returns>True when the item is one of the five shapes the grammar allows.</returns>
    private static bool IsValidItem(string item, int minimum, int maximum)
    {
        if (item.Length == 0)
        {
            return false;
        }

        // The FIRST slash, so `1-5/2/3` reads as base `1-5` with step `2/3` and is refused for the
        // step rather than being silently read as something shorter.
        var slash = item.IndexOf('/', StringComparison.Ordinal);
        var basePart = slash < 0 ? item : item[..slash];
        var stepPart = slash < 0 ? null : item[(slash + 1)..];

        if (!TryReadBase(basePart, minimum, maximum, out var baseCarriesASpan))
        {
            return false;
        }

        if (stepPart is null)
        {
            return true;
        }

        // A step needs a span to step across, so the grammar allows it after `*` and after a range
        // and nowhere else. `5/2` is refused rather than read as "every second value from 5
        // onwards", which is what some crons make of it and others reject outright.
        if (!baseCarriesASpan)
        {
            return false;
        }

        if (!TryReadNumber(stepPart, out var step))
        {
            return false;
        }

        // The SPAN, not the maximum. A step starts at the low bound, so it names a second value only
        // when minimum + step is still inside the field; the two quantities are equal only in the
        // three zero-based fields, which is why this is written out rather than compared to maximum.
        return step >= 1 && step <= maximum - minimum;
    }

    /// <summary>Reads the part of an item before any step, and says whether it spans values.</summary>
    /// <param name="basePart">The item with its step removed.</param>
    /// <param name="minimum">The smallest number this field accepts.</param>
    /// <param name="maximum">The largest number this field accepts.</param>
    /// <param name="carriesASpan">Set to true when the base is a wildcard or a range.</param>
    /// <returns>True when the base is acceptable on its own.</returns>
    private static bool TryReadBase(string basePart, int minimum, int maximum, out bool carriesASpan)
    {
        carriesASpan = false;

        if (string.Equals(basePart, Wildcard, StringComparison.Ordinal))
        {
            carriesASpan = true;
            return true;
        }

        var dash = basePart.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return TryReadBounded(basePart, minimum, maximum, out _);
        }

        if (!TryReadBounded(basePart[..dash], minimum, maximum, out var low))
        {
            return false;
        }

        if (!TryReadBounded(basePart[(dash + 1)..], minimum, maximum, out var high))
        {
            return false;
        }

        carriesASpan = true;

        return low <= high;
    }

    /// <summary>Reads a number and checks it against its field's inclusive bounds.</summary>
    /// <param name="text">The digits as written.</param>
    /// <param name="minimum">The smallest number this field accepts.</param>
    /// <param name="maximum">The largest number this field accepts.</param>
    /// <param name="value">The parsed value, when the return is true.</param>
    /// <returns>True when the text is a bare number inside the field's bounds.</returns>
    private static bool TryReadBounded(string text, int minimum, int maximum, out int value)
    {
        return TryReadNumber(text, out value) && value >= minimum && value <= maximum;
    }

    /// <summary>Reads a bare decimal number: no sign, no padding, at most three digits.</summary>
    /// <param name="text">The digits as written.</param>
    /// <param name="value">The parsed value, when the return is true.</param>
    /// <returns>True when the text is one to three unpadded ASCII digits.</returns>
    /// <remarks>
    /// The digits are checked by hand before parsing rather than handed straight to
    /// <see cref="int.Parse(string, IFormatProvider)"/>, which accepts a leading sign, surrounding
    /// whitespace and digits from other scripts — any of which would let a value through a field
    /// that must hold ASCII digits and nothing else. The leading-zero refusal is what keeps one
    /// schedule to one text, so a stored schedule and a later read compare equal.
    /// </remarks>
    private static bool TryReadNumber(string text, out int value)
    {
        value = 0;

        if (text.Length == 0 || text.Length > MaximumNumberDigits)
        {
            return false;
        }

        if (text.Length > 1 && text[0] == '0')
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        value = int.Parse(text, CultureInfo.InvariantCulture);

        return true;
    }
}
