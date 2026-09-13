//! Tests for the `cron_schedule` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `cron_schedule.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach the private field rules a crate-level
//! `tests/` directory could not see.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CronSchedule, CronScheduleError, MAX_FIELD_LENGTH};

/// Every accepted item shape, in the minute field, with the other four left as
/// wildcards.
fn minute(field: &str) -> Result<CronSchedule, CronScheduleError> {
    CronSchedule::parse(field, "*", "*", "*", "*")
}

#[test]
fn every_conventional_form_parses_and_renders_itself() {
    for field in ["*", "*/5", "1", "1-5", "1-5/2", "0,30", "1-5/2,10"] {
        let schedule = minute(field).unwrap();
        assert_eq!(schedule.minute(), field);
        assert_eq!(schedule.to_string(), format!("{field} * * * *"));
    }
}

#[test]
fn each_field_keeps_its_own_value() {
    let schedule = CronSchedule::parse("5", "6", "7", "8", "3").unwrap();

    assert_eq!(schedule.minute(), "5");
    assert_eq!(schedule.hour(), "6");
    assert_eq!(schedule.day_of_month(), "7");
    assert_eq!(schedule.month(), "8");
    assert_eq!(schedule.day_of_week(), "3");
}

#[test]
fn display_is_exactly_five_fields_space_separated() {
    let schedule = CronSchedule::parse("*/15", "0-6", "1", "1,7", "0").unwrap();

    assert_eq!(schedule.to_string(), "*/15 0-6 1 1,7 0");
    assert_eq!(schedule.to_string().split(' ').count(), 5);
}

#[test]
fn a_field_outside_its_bounds_is_refused() {
    assert_eq!(
        CronSchedule::parse("60", "*", "*", "*", "*"),
        Err(CronScheduleError::OutOfRange {
            field: "minute",
            value: 60,
            minimum: 0,
            maximum: 59,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "24", "*", "*", "*"),
        Err(CronScheduleError::OutOfRange {
            field: "hour",
            value: 24,
            minimum: 0,
            maximum: 23,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "0", "*", "*"),
        Err(CronScheduleError::OutOfRange {
            field: "day of month",
            value: 0,
            minimum: 1,
            maximum: 31,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "13", "*"),
        Err(CronScheduleError::OutOfRange {
            field: "month",
            value: 13,
            minimum: 1,
            maximum: 12,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*", "7"),
        Err(CronScheduleError::OutOfRange {
            field: "day of week",
            value: 7,
            minimum: 0,
            maximum: 6,
        })
    );
}

#[test]
fn a_bound_is_checked_at_both_ends_of_a_range() {
    assert_eq!(
        minute("0-60"),
        Err(CronScheduleError::OutOfRange {
            field: "minute",
            value: 60,
            minimum: 0,
            maximum: 59,
        })
    );
}

#[test]
fn a_reversed_range_is_refused() {
    assert_eq!(
        minute("5-1"),
        Err(CronScheduleError::ReversedRange {
            field: "minute",
            low: 5,
            high: 1,
        })
    );
}

#[test]
fn an_equal_range_is_accepted() {
    assert_eq!(minute("5-5").unwrap().minute(), "5-5");
}

#[test]
fn a_zero_step_is_refused() {
    assert_eq!(
        minute("*/0"),
        Err(CronScheduleError::ZeroStep { field: "minute" })
    );
    assert_eq!(
        minute("1-5/0"),
        Err(CronScheduleError::ZeroStep { field: "minute" })
    );
}

#[test]
fn a_step_larger_than_the_field_is_refused() {
    // `*/60` in the minute field steps past the end of the span on its first
    // stride, so it names minute 0 and nothing else — `0` written the long way.
    assert_eq!(
        minute("*/60"),
        Err(CronScheduleError::StepTooLarge {
            field: "minute",
            step: 60,
            largest_step: 59,
        })
    );
    assert_eq!(
        minute("*/999"),
        Err(CronScheduleError::StepTooLarge {
            field: "minute",
            step: 999,
            largest_step: 59,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*", "*/7"),
        Err(CronScheduleError::StepTooLarge {
            field: "day of week",
            step: 7,
            largest_step: 6,
        })
    );

    // The largest step each field does accept.
    assert_eq!(minute("*/59").unwrap().minute(), "*/59");
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*", "*/6")
            .unwrap()
            .day_of_week(),
        "*/6"
    );
}

#[test]
fn the_step_bound_is_the_span_not_the_maximum_on_a_one_based_field() {
    // The case the three assertions above cannot see. A step starts at the LOW
    // bound, so it names a second value only when `minimum + step` is still
    // inside the field — the bound is `maximum - minimum`. On minute, hour and
    // day-of-week that equals the maximum, because their minimum is 0; on
    // day-of-month (1-31) and month (1-12) it does not, and comparing against
    // the maximum admits `*/31` and `*/12`, each of which selects the low bound
    // alone. These four assertions are what tell the two formulas apart.
    assert_eq!(
        CronSchedule::parse("*", "*", "*/31", "*", "*"),
        Err(CronScheduleError::StepTooLarge {
            field: "day of month",
            step: 31,
            largest_step: 30,
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*/12", "*"),
        Err(CronScheduleError::StepTooLarge {
            field: "month",
            step: 12,
            largest_step: 11,
        })
    );

    // And the largest step each of those two fields still accepts: `*/30` names
    // days 1 and 31, `*/11` names January and December.
    assert_eq!(
        CronSchedule::parse("*", "*", "*/30", "*", "*")
            .unwrap()
            .day_of_month(),
        "*/30"
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*/11", "*")
            .unwrap()
            .month(),
        "*/11"
    );
}

#[test]
fn a_number_padded_with_a_leading_zero_is_refused() {
    // One schedule, one text: `0-7` and `0-007` must not both be storable, or
    // the later read that compares an entry against its schedule has two
    // answers. `SourceCidr` refuses `010.0.0.1` and `/032` for the same reason.
    for (field, item) in [
        ("007", "007"),
        ("0-007", "007"),
        ("00", "00"),
        ("*/05", "05"),
    ] {
        assert_eq!(
            minute(field),
            Err(CronScheduleError::LeadingZero {
                field: "minute",
                item: item.to_owned(),
            })
        );
    }

    // A bare zero is the number, not a padding of it.
    assert_eq!(minute("0").unwrap().minute(), "0");
    assert_eq!(minute("0-7").unwrap().minute(), "0-7");
}

#[test]
fn a_step_on_a_bare_number_is_refused() {
    assert_eq!(
        minute("5/2"),
        Err(CronScheduleError::Malformed {
            field: "minute",
            item: "5/2".to_owned(),
        })
    );
}

#[test]
fn names_and_at_shortcuts_are_refused() {
    for field in ["JAN", "jan", "MON", "@daily", "@reboot"] {
        assert_eq!(
            minute(field),
            Err(CronScheduleError::Malformed {
                field: "minute",
                item: field.to_owned(),
            })
        );
    }
}

#[test]
fn a_signed_number_is_refused() {
    assert_eq!(
        minute("+5"),
        Err(CronScheduleError::Malformed {
            field: "minute",
            item: "+5".to_owned(),
        })
    );
}

#[test]
fn whitespace_anywhere_in_a_field_is_refused() {
    for field in ["1 2", " 1", "1 ", "1,\t2"] {
        assert!(matches!(
            minute(field),
            Err(CronScheduleError::Whitespace { .. } | CronScheduleError::ControlCharacter { .. })
        ));
    }

    assert_eq!(
        minute("1 2"),
        Err(CronScheduleError::Whitespace { field: "minute" })
    );
}

#[test]
fn a_newline_is_refused_as_the_control_character_it_is() {
    assert_eq!(
        minute("1\n2"),
        Err(CronScheduleError::ControlCharacter {
            field: "minute",
            character: '\n',
        })
    );
    assert_eq!(
        CronSchedule::parse("*", "*", "*", "*", "1\r"),
        Err(CronScheduleError::ControlCharacter {
            field: "day of week",
            character: '\r',
        })
    );
}

#[test]
fn an_empty_field_or_item_is_refused() {
    assert_eq!(
        minute(""),
        Err(CronScheduleError::Empty { field: "minute" })
    );
    assert_eq!(
        minute("1,,2"),
        Err(CronScheduleError::Empty { field: "minute" })
    );
    assert_eq!(
        minute("1,"),
        Err(CronScheduleError::Empty { field: "minute" })
    );
}

#[test]
fn a_field_longer_than_any_schedule_needs_is_refused() {
    let candidate = "1".repeat(MAX_FIELD_LENGTH + 1);

    assert_eq!(
        minute(&candidate),
        Err(CronScheduleError::TooLong {
            field: "minute",
            maximum: MAX_FIELD_LENGTH,
        })
    );
}

#[test]
fn a_fully_enumerated_minute_field_still_fits() {
    let candidate = (0..60)
        .map(|value| value.to_string())
        .collect::<Vec<_>>()
        .join(",");

    assert_eq!(minute(&candidate).unwrap().minute(), candidate);
}

#[test]
fn a_number_of_more_digits_than_any_field_holds_is_refused() {
    assert_eq!(
        minute("1000"),
        Err(CronScheduleError::Malformed {
            field: "minute",
            item: "1000".to_owned(),
        })
    );
}
