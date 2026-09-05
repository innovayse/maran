//! Why a cron schedule field was refused.

/// Reasons [`super::cron_schedule::CronSchedule::parse`] refuses a schedule.
///
/// Every variant names the field it is about, because a schedule is five
/// separate values and a bare "invalid schedule" leaves the operator guessing
/// which one. The field name is one of five agent-owned literals and never
/// caller text, so naming it discloses nothing the caller did not send.
///
/// The variants are deliberately specific rather than a single `Invalid`: a
/// schedule is written by an operator through the panel, not offered by an
/// attacker probing the shape of the rules, and "the minute field cannot hold
/// 60" is the difference between a fixed form and a support ticket.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CronScheduleError {
    /// The field, or one comma-separated item inside it, was empty.
    ///
    /// Covers `""`, `"1,,2"` and `"1,"` alike: cron reads an empty field as a
    /// missing one and silently shifts every field after it, so a crontab line
    /// built from one would run at a time nobody chose.
    #[error("the {field} field cannot be empty")]
    Empty {
        /// Which of the five fields.
        field: &'static str,
    },

    /// The field was longer than any legal schedule needs.
    ///
    /// A bound exists because the field is written verbatim into a root-owned
    /// crontab line, and an unbounded one is a line of unbounded length for no
    /// expressible gain.
    #[error("the {field} field cannot exceed {maximum} characters")]
    TooLong {
        /// Which of the five fields.
        field: &'static str,
        /// The ceiling that was exceeded.
        maximum: usize,
    },

    /// A control character — a newline, a carriage return, a NUL — was found.
    ///
    /// Refused by name and before the shape checks, rather than as a
    /// consequence of them, because this is the character class the type exists
    /// for: a newline inside a field ends the crontab line and starts one of
    /// the caller's choosing, in a file cron runs (rules/security.md §4). A
    /// refusal that only happens implicitly is one a later loosening of the
    /// grammar silently removes.
    #[error("the {field} field cannot contain `{character:?}`")]
    ControlCharacter {
        /// Which of the five fields.
        field: &'static str,
        /// The first offending character.
        character: char,
    },

    /// A space or a tab was found inside the field.
    ///
    /// Also refused by name: cron separates fields by whitespace, so a space
    /// inside one turns five fields into six and shifts the command into the
    /// schedule. The five fields are supplied separately to
    /// [`super::cron_schedule::CronSchedule::parse`] precisely so that this is
    /// a refusal rather than a re-split.
    #[error("the {field} field cannot contain whitespace")]
    Whitespace {
        /// Which of the five fields.
        field: &'static str,
    },

    /// An item was not `*`, `N`, `N-M`, `*/step` or `N-M/step`.
    ///
    /// Month and day names (`JAN`, `MON`), `@daily` and friends, and a step on
    /// a bare number (`5/2`) all land here. Names and shortcuts are refused
    /// rather than translated because the agent renders the field verbatim: a
    /// value it does not fully understand is a value it cannot promise the
    /// meaning of.
    #[error("`{item}` is not a valid {field} item")]
    Malformed {
        /// Which of the five fields.
        field: &'static str,
        /// The offending comma-separated item.
        item: String,
    },

    /// A number fell outside the range its field allows.
    #[error("`{value}` is outside the {minimum}-{maximum} range the {field} field allows")]
    OutOfRange {
        /// Which of the five fields.
        field: &'static str,
        /// The offending number.
        value: u32,
        /// Smallest number the field accepts.
        minimum: u8,
        /// Largest number the field accepts.
        maximum: u8,
    },

    /// A range ran backwards — `5-1`.
    ///
    /// Cron reads such a range as an empty set, so the entry silently never
    /// runs. Refusing it turns a job that quietly does nothing into an error
    /// the operator sees while they are still looking at the form.
    #[error("the {field} range `{low}-{high}` runs backwards")]
    ReversedRange {
        /// Which of the five fields.
        field: &'static str,
        /// The lower bound as written.
        low: u32,
        /// The upper bound as written.
        high: u32,
    },

    /// A number was written with a leading zero — `007`.
    ///
    /// `0-7` and `0-007` are two texts for one schedule, and the schedule is
    /// what a later read compares an entry against. The sibling
    /// [`super::super::web::source_cidr::SourceCidr`] refuses `010.0.0.1` and
    /// `/032` for exactly this reason; one rule deserves one answer across the
    /// crate. A bare `0` is of course accepted — it is the number, not a
    /// padding of it.
    #[error("the {field} field cannot pad `{item}` with a leading zero")]
    LeadingZero {
        /// Which of the five fields.
        field: &'static str,
        /// The offending number.
        item: String,
    },

    /// A step was larger than the distance between the field's bounds —
    /// `*/60` in minutes, `*/31` in day-of-month.
    ///
    /// Such a step steps past the end of the span on its first stride, so it
    /// names the low bound and nothing else — `*/60` in the minute field is
    /// `0` written the long way, and `*/31` in day-of-month is `1`. Refused for
    /// the same reason `7` for Sunday and `5/2` are: the agent renders what it
    /// validated, and a value whose meaning is "not what it looks like" is one
    /// the operator did not mean to write.
    ///
    /// The bound is the SPAN and not the field's maximum, which matters only on
    /// the two 1-based fields — day-of-month and month — where the two
    /// quantities differ. On the three 0-based fields they coincide, which is
    /// how a version of this check that compared against the maximum passed a
    /// test made only of 0-based cases.
    #[error("the {field} step cannot exceed {largest_step}")]
    StepTooLarge {
        /// Which of the five fields.
        field: &'static str,
        /// The offending step.
        step: u32,
        /// Largest step that still names more than one value: `maximum - minimum`.
        largest_step: u8,
    },

    /// A step was `0` — `*/0`.
    ///
    /// A step of zero divides the span into nothing; cron's own parsers differ
    /// on whether that is an error or an every-value wildcard, and an entry
    /// whose meaning depends on which cron the host ships is not a schedule.
    #[error("the {field} step must be at least 1")]
    ZeroStep {
        /// Which of the five fields.
        field: &'static str,
    },
}
