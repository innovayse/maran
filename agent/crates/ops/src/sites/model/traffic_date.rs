//! A calendar day in UTC, as the traffic summary buckets requests.

/// Seconds in one day.
const SECONDS_PER_DAY: i64 = 86_400;

/// One UTC calendar day, stored as the number of days since 1970-01-01.
///
/// A day number and not a string so a tally can order and key by it, and so the
/// only code that knows the calendar is this type. The conversion from a log
/// line's local timestamp goes through the line's own UTC offset: a request
/// logged at `00:30 +0300` on the 8th happened on the 7th in UTC, and the
/// summary is bucketed in UTC so two servers in different zones agree.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct TrafficDate(i64);

impl TrafficDate {
    /// The UTC day of a local timestamp.
    ///
    /// `offset_seconds` is the zone the timestamp was written in (`+0300` is
    /// `10_800`); it is subtracted to reach UTC. Returns `None` for a month
    /// outside `1..=12`, a day outside `1..=31`, or a clock reading outside a
    /// day: a line that claims such a time is refused rather than rounded into
    /// a day it did not happen on.
    #[must_use]
    pub fn from_local(
        year: i64,
        month: u32,
        day: u32,
        seconds_into_day: u32,
        offset_seconds: i64,
    ) -> Option<Self> {
        if !(1..=12).contains(&month)
            || !(1..=31).contains(&day)
            || i64::from(seconds_into_day) >= SECONDS_PER_DAY
        {
            return None;
        }

        let local = days_from_civil(year, month, day)
            .checked_mul(SECONDS_PER_DAY)?
            .checked_add(i64::from(seconds_into_day))?;
        let utc = local.checked_sub(offset_seconds)?;

        Some(Self(utc.div_euclid(SECONDS_PER_DAY)))
    }

    /// The day as `YYYY-MM-DD`.
    #[must_use]
    pub fn to_iso(&self) -> String {
        let (year, month, day) = civil_from_days(self.0);
        format!("{year:04}-{month:02}-{day:02}")
    }
}

/// Days since 1970-01-01 of a proleptic Gregorian date.
///
/// The standard era-based algorithm, exact for every date and with no table.
fn days_from_civil(year: i64, month: u32, day: u32) -> i64 {
    let year = if month <= 2 { year - 1 } else { year };
    let era = year.div_euclid(400);
    let year_of_era = year.rem_euclid(400);
    let month_index = i64::from((month + 9) % 12);
    let day_of_year = (153 * month_index + 2) / 5 + i64::from(day) - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;

    era * 146_097 + day_of_era - 719_468
}

/// The proleptic Gregorian date of a day count since 1970-01-01.
///
/// The inverse of [`days_from_civil`].
fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let shifted = days + 719_468;
    let era = shifted.div_euclid(146_097);
    let day_of_era = shifted.rem_euclid(146_097);
    let year_of_era =
        (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_index = (5 * day_of_year + 2) / 153;
    let day = u32::try_from(day_of_year - (153 * month_index + 2) / 5 + 1).unwrap_or(1);
    let month = u32::try_from(if month_index < 10 {
        month_index + 3
    } else {
        month_index - 9
    })
    .unwrap_or(1);
    let year = year_of_era + era * 400 + i64::from(month <= 2);

    (year, month, day)
}

#[cfg(test)]
#[path = "../../tests/sites/model/traffic_date_tests.rs"]
mod tests;
