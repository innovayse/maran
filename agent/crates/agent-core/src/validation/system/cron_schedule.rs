//! Cron schedule validation: the five time fields of a crontab line.

use std::fmt;

use super::cron_schedule_error::CronScheduleError;

/// The most characters one field may be.
///
/// A ceiling on the rendered line, and only that. It is set above the longest
/// field a caller can need — a fully enumerated minute field, `0,1,2,…,59`, is
/// 169 characters — so nothing a crontab can express is refused by length. What
/// it deliberately does NOT claim is that everything under it says something
/// new: items are neither deduplicated nor merged, so `1,1,1,…` and
/// `1-5,2-3,…` can spend the remaining characters restating a schedule they
/// have already named, and a five-field schedule can therefore reach five times
/// this length while the longest MEANINGFUL one is far shorter.
///
/// That is a deliberate limit on the type's ambition rather than an oversight.
/// Deciding which of several overlapping spellings of one schedule is the good
/// one is a normalisation problem, and a normalising validator would render
/// something other than what the operator typed — which the round-trip property
/// this type is built on forbids. The bound that matters here is the one this
/// constant does deliver: a caller cannot make the crontab line arbitrarily
/// long, whatever they send.
const MAX_FIELD_LENGTH: usize = 256;

/// The most digits any single number in a field may have.
///
/// The largest value any field accepts is `59`, so three digits is already one
/// more than a legal schedule needs. The cap is what makes the digit fold in
/// [`parse_number`] provably free of overflow, so the module needs no
/// unreachable error arm to satisfy the ban on `unwrap`.
const MAX_NUMBER_DIGITS: usize = 3;

/// The item that means "every value this field has".
const WILDCARD: &str = "*";

/// One field's reported name and the inclusive range cron accepts in it.
///
/// Private, and passed by reference to the parsing helpers, so the five fields
/// are five values rather than five copies of the same code. Every instance is
/// one of the five constants below, and each has `maximum` above `minimum`,
/// which is what makes the subtraction in [`FieldRule::largest_step`] safe.
struct FieldRule {
    /// The name a rejection reports the field by.
    name: &'static str,
    /// Smallest number the field accepts.
    minimum: u8,
    /// Largest number the field accepts.
    maximum: u8,
}

impl FieldRule {
    /// The largest step this field can carry and still name more than one value.
    ///
    /// The SPAN, `maximum - minimum`, not the maximum: a step starts at the low
    /// bound, so it names a second value only when `minimum + step` is still
    /// inside the field. The two quantities are equal exactly where `minimum`
    /// is zero, which is three of the five fields — and that coincidence is why
    /// an earlier version of this bound compared against `maximum` and let
    /// `*/31` in day-of-month and `*/12` in month through, each selecting the
    /// low bound alone. A test that only exercises 0-based fields cannot tell
    /// the two formulas apart, so `largest_step` is named and used rather than
    /// inlined, and the test that guards it names a 1-based field.
    fn largest_step(&self) -> u8 {
        self.maximum - self.minimum
    }
}

/// Minutes past the hour.
const MINUTE: FieldRule = FieldRule {
    name: "minute",
    minimum: 0,
    maximum: 59,
};

/// Hours of the day, on a 24-hour clock.
const HOUR: FieldRule = FieldRule {
    name: "hour",
    minimum: 0,
    maximum: 23,
};

/// Days of the month. One-based, unlike every other field but the month.
const DAY_OF_MONTH: FieldRule = FieldRule {
    name: "day of month",
    minimum: 1,
    maximum: 31,
};

/// Months of the year, one-based.
const MONTH: FieldRule = FieldRule {
    name: "month",
    minimum: 1,
    maximum: 12,
};

/// Days of the week, `0` = Sunday.
///
/// Capped at `6` rather than at the `7` some crons also accept for Sunday: the
/// agent renders what it validated, and two spellings of one day is a value
/// whose meaning depends on which cron the host ships.
const DAY_OF_WEEK: FieldRule = FieldRule {
    name: "day of week",
    minimum: 0,
    maximum: 6,
};

/// A validated cron schedule — the five time fields of one crontab line.
///
/// The fields are private and the only constructor is [`CronSchedule::parse`],
/// so holding a value of this type is proof that validation happened. That
/// matters more here than for most values: the schedule is the ONLY part of the
/// installed crontab line that comes from a request at all. Everything else on
/// that line is an agent constant plus an agent-minted entry id, and the
/// customer's command lives in a file rather than on the line, so a
/// `CronSchedule` that cannot hold a space, a newline or a `%` is what makes
/// "no caller-supplied byte reaches the crontab" a property of the types rather
/// than a promise in a comment.
///
/// Each field is stored exactly as it was written, and [`fmt::Display`] renders
/// the five back space-separated: the value round-trips, so what an operator
/// typed is what the crontab shows and what a later read compares against.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct CronSchedule {
    /// The minute field, as written.
    minute: String,
    /// The hour field, as written.
    hour: String,
    /// The day-of-month field, as written.
    day_of_month: String,
    /// The month field, as written.
    month: String,
    /// The day-of-week field, as written.
    day_of_week: String,
}

impl CronSchedule {
    /// Validates the five fields of a crontab schedule and wraps them.
    ///
    /// Each field is a comma-separated list of items, and an item is one of
    /// `*`, `*/step`, `N`, `N-M` or `N-M/step`. Numbers are decimal and bounded
    /// per field (`0-59`, `0-23`, `1-31`, `1-12`, `0-6`) and unpadded; a range
    /// must not run backwards; a step is at least `1`, at most the distance
    /// between the field's bounds, and may only follow `*` or a range, because
    /// a step needs a span to step across.
    ///
    /// Deliberately absent: month and weekday names, `@hourly` and its
    /// relatives, and whitespace of any kind. The agent renders the field
    /// verbatim into a crontab line, so it accepts only what it can fully
    /// account for — and the five fields arrive as five arguments precisely so
    /// that a space cannot smuggle a sixth.
    ///
    /// Written as explicit character checks rather than a regex, like every
    /// other validator here: the grammar is short enough to read directly, and
    /// this way the crate needs neither a regex dependency nor the
    /// lazily-compiled pattern whose "this literal cannot fail to compile"
    /// unwrap the agent is not allowed to write.
    ///
    /// # Errors
    ///
    /// - [`CronScheduleError::Empty`] when a field, or an item inside one, is
    ///   empty.
    /// - [`CronScheduleError::TooLong`] when a field exceeds 256 characters.
    /// - [`CronScheduleError::ControlCharacter`] for a newline, a carriage
    ///   return or any other control character — the injection this type exists
    ///   to stop.
    /// - [`CronScheduleError::Whitespace`] for a space or a tab inside a field.
    /// - [`CronScheduleError::Malformed`] for an item that is not one of the
    ///   five shapes, including names and `@shortcuts`.
    /// - [`CronScheduleError::LeadingZero`] when a multi-digit number is padded
    ///   with a zero.
    /// - [`CronScheduleError::OutOfRange`] when a number falls outside its
    ///   field's bounds.
    /// - [`CronScheduleError::ReversedRange`] when a range runs backwards.
    /// - [`CronScheduleError::ZeroStep`] when a step is `0`.
    /// - [`CronScheduleError::StepTooLarge`] when a step exceeds the distance
    ///   between the field's bounds, which makes it name the low bound and
    ///   nothing else.
    pub fn parse(
        minute: &str,
        hour: &str,
        day_of_month: &str,
        month: &str,
        day_of_week: &str,
    ) -> Result<Self, CronScheduleError> {
        parse_field(&MINUTE, minute)?;
        parse_field(&HOUR, hour)?;
        parse_field(&DAY_OF_MONTH, day_of_month)?;
        parse_field(&MONTH, month)?;
        parse_field(&DAY_OF_WEEK, day_of_week)?;

        Ok(Self {
            minute: minute.to_owned(),
            hour: hour.to_owned(),
            day_of_month: day_of_month.to_owned(),
            month: month.to_owned(),
            day_of_week: day_of_week.to_owned(),
        })
    }

    /// The minute field, as written.
    #[must_use]
    pub fn minute(&self) -> &str {
        &self.minute
    }

    /// The hour field, as written.
    #[must_use]
    pub fn hour(&self) -> &str {
        &self.hour
    }

    /// The day-of-month field, as written.
    #[must_use]
    pub fn day_of_month(&self) -> &str {
        &self.day_of_month
    }

    /// The month field, as written.
    #[must_use]
    pub fn month(&self) -> &str {
        &self.month
    }

    /// The day-of-week field, as written.
    #[must_use]
    pub fn day_of_week(&self) -> &str {
        &self.day_of_week
    }
}

impl fmt::Display for CronSchedule {
    /// Renders the five fields separated by single spaces — exactly the leading
    /// text of a crontab line, and nothing else.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            formatter,
            "{} {} {} {} {}",
            self.minute, self.hour, self.day_of_month, self.month, self.day_of_week
        )
    }
}

/// Validates one whole field against its rule.
///
/// The character-class refusals come first and by name, so that loosening the
/// grammar below can never quietly re-admit a newline.
///
/// # Errors
///
/// As documented on [`CronSchedule::parse`].
fn parse_field(rule: &FieldRule, candidate: &str) -> Result<(), CronScheduleError> {
    if candidate.is_empty() {
        return Err(CronScheduleError::Empty { field: rule.name });
    }

    if candidate.len() > MAX_FIELD_LENGTH {
        return Err(CronScheduleError::TooLong {
            field: rule.name,
            maximum: MAX_FIELD_LENGTH,
        });
    }

    if let Some(character) = candidate.chars().find(|c| c.is_control()) {
        return Err(CronScheduleError::ControlCharacter {
            field: rule.name,
            character,
        });
    }

    if candidate.chars().any(char::is_whitespace) {
        return Err(CronScheduleError::Whitespace { field: rule.name });
    }

    for item in candidate.split(',') {
        parse_item(rule, item)?;
    }

    Ok(())
}

/// Validates one comma-separated item of a field.
///
/// # Errors
///
/// As documented on [`CronSchedule::parse`].
fn parse_item(rule: &FieldRule, item: &str) -> Result<(), CronScheduleError> {
    if item.is_empty() {
        return Err(CronScheduleError::Empty { field: rule.name });
    }

    let (base, step) = match item.split_once('/') {
        Some((base, step)) => (base, Some(step)),
        None => (item, None),
    };

    // A step needs a span to step across, so the grammar allows it after `*` and
    // after a range and nowhere else. `5/2` is refused rather than read as
    // "every second value from 5 onwards", which is what some crons make of it
    // and others reject outright.
    let base_carries_a_span = if base == WILDCARD {
        true
    } else if let Some((low, high)) = base.split_once('-') {
        let low = parse_bounded(rule, low)?;
        let high = parse_bounded(rule, high)?;
        if low > high {
            return Err(CronScheduleError::ReversedRange {
                field: rule.name,
                low,
                high,
            });
        }
        true
    } else {
        parse_bounded(rule, base)?;
        false
    };

    let Some(step) = step else {
        return Ok(());
    };

    if !base_carries_a_span {
        return Err(CronScheduleError::Malformed {
            field: rule.name,
            item: item.to_owned(),
        });
    }

    let step = parse_number(rule, step)?;
    if step == 0 {
        return Err(CronScheduleError::ZeroStep { field: rule.name });
    }
    let largest_step = rule.largest_step();
    if step > u32::from(largest_step) {
        return Err(CronScheduleError::StepTooLarge {
            field: rule.name,
            step,
            largest_step,
        });
    }

    Ok(())
}

/// Parses a number and checks it against its field's inclusive bounds.
///
/// # Errors
///
/// As documented on [`CronSchedule::parse`].
fn parse_bounded(rule: &FieldRule, text: &str) -> Result<u32, CronScheduleError> {
    let value = parse_number(rule, text)?;

    if value < u32::from(rule.minimum) || value > u32::from(rule.maximum) {
        return Err(CronScheduleError::OutOfRange {
            field: rule.name,
            value,
            minimum: rule.minimum,
            maximum: rule.maximum,
        });
    }

    Ok(value)
}

/// Reads a bare decimal number, with no sign, no padding and no overflow.
///
/// The digits are folded by hand rather than handed to `str::parse`, which
/// accepts a leading `+` and would let `+5` through a field that must hold
/// digits and nothing else. The three-digit cap keeps the fold below `999`, so
/// it cannot overflow and needs no error arm no test could reach.
///
/// # Errors
///
/// - [`CronScheduleError::Malformed`] when `text` is empty, longer than three
///   digits, or holds anything but ASCII digits.
/// - [`CronScheduleError::LeadingZero`] when a multi-digit number is padded
///   with a zero, so that one schedule has one text.
fn parse_number(rule: &FieldRule, text: &str) -> Result<u32, CronScheduleError> {
    let malformed = || CronScheduleError::Malformed {
        field: rule.name,
        item: text.to_owned(),
    };

    if text.is_empty() || text.len() > MAX_NUMBER_DIGITS {
        return Err(malformed());
    }

    if text.len() > 1 && text.starts_with('0') {
        return Err(CronScheduleError::LeadingZero {
            field: rule.name,
            item: text.to_owned(),
        });
    }

    let mut value: u32 = 0;
    for byte in text.bytes() {
        if !byte.is_ascii_digit() {
            return Err(malformed());
        }
        value = value * 10 + u32::from(byte - b'0');
    }

    Ok(value)
}

#[cfg(test)]
#[path = "../../tests/validation/system/cron_schedule_tests.rs"]
mod tests;
