//! When the installed certificate stops being accepted.

use maran_distro::DistroAdapter;

use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Prints only the `notAfter` field of the certificate on standard input.
const END_DATE: [&str; 3] = ["x509", "-noout", "-enddate"];

/// The prefix openssl puts in front of the date.
const END_DATE_PREFIX: &str = "notAfter=";

/// The only timezone openssl prints a certificate date in.
///
/// A certificate stores its validity in UTC, and openssl renders it as `GMT`.
/// Any other suffix means the output is not the format this parser was written
/// against, and a date read out of a format nobody checked is worse than no
/// date: it becomes a renewal scheduled for the wrong day.
const UTC_SUFFIX: &str = "GMT";

/// Seconds in a day, for turning a civil date into a Unix timestamp.
const SECONDS_PER_DAY: i64 = 86_400;

/// The expiry of `material`'s certificate, as Unix seconds.
///
/// This is the number the whole area exists to return: `InstallCertificateOk`
/// carries it, and the panel schedules renewal 30 days before it (spec §11).
/// Read from the certificate that was actually installed rather than from
/// anything the caller said about it — the panel's own idea of the expiry is a
/// second copy of a fact, and the copy is the one that goes stale.
///
/// Parsed here rather than with a date library, and deliberately: the whole
/// input is one field of one tool's output in one format, `MMM D HH:MM:SS YYYY
/// GMT`, and a certificate's validity is always UTC. What a library would add
/// is timezone handling this must not have.
///
/// # Errors
///
/// Returns [`SslOpError::ToolUnavailable`] when openssl cannot be run,
/// [`SslOpError::MalformedCertificate`] when it will not read the certificate,
/// and [`SslOpError::ExpiryUnreadable`] when the date it printed is not the
/// format above — never a guessed expiry, which would be a site that silently
/// stops working on a day nobody has in a calendar.
pub(crate) fn certificate_expiry(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    material: &CertificateMaterial,
) -> Result<i64, SslOpError> {
    let outcome = host.run_with_certificate(
        distro.openssl_binary(),
        &END_DATE,
        material.certificate_pem(),
    )?;
    if outcome.status != 0 {
        return Err(SslOpError::MalformedCertificate {
            // openssl's own words, unfiltered: this process was fed the
            // certificate and never the key, so there is nothing here it could
            // have echoed that is not already public.
            reason: outcome.stderr,
        });
    }

    let printed = outcome.stdout.trim();
    parse_end_date(printed).ok_or_else(|| SslOpError::ExpiryUnreadable {
        // The certificate's own date, which is public information printed by a
        // tool that was handed only the public half.
        reason: format!("openssl printed `{printed}`"),
    })
}

/// Turns `notAfter=Aug 30 12:00:00 2099 GMT` into Unix seconds.
///
/// Returns `None` for anything that is not exactly that shape, including a
/// month name that is not one of the twelve and a timezone that is not `GMT` —
/// every one of those is a format this function was not written against, and
/// guessing at one produces a plausible number that is wrong.
fn parse_end_date(printed: &str) -> Option<i64> {
    let date = printed.strip_prefix(END_DATE_PREFIX)?;

    // `split_whitespace` rather than a split on a single space: openssl pads a
    // single-digit day to two columns, so the ninth of a month arrives as
    // `Aug  9` with two spaces.
    let mut fields = date.split_whitespace();
    let month = month_number(fields.next()?)?;
    let day: i64 = fields.next()?.parse().ok()?;
    let time = fields.next()?;
    let year: i64 = fields.next()?.parse().ok()?;
    if fields.next()? != UTC_SUFFIX || fields.next().is_some() {
        return None;
    }

    let mut clock = time.split(':');
    let hour: i64 = clock.next()?.parse().ok()?;
    let minute: i64 = clock.next()?.parse().ok()?;
    let second: i64 = clock.next()?.parse().ok()?;
    if clock.next().is_some() || hour > 23 || minute > 59 || second > 60 {
        return None;
    }

    let days = days_from_civil(year, month, day)?;

    Some(days * SECONDS_PER_DAY + hour * 3600 + minute * 60 + second)
}

/// The month's number, 1–12, from the three-letter English abbreviation
/// openssl prints.
fn month_number(name: &str) -> Option<i64> {
    const MONTHS: [&str; 12] = [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    ];

    MONTHS
        .iter()
        .position(|month| *month == name)
        .and_then(|index| i64::try_from(index + 1).ok())
}

/// Days between 1970-01-01 and the given proleptic Gregorian date.
///
/// Howard Hinnant's `days_from_civil`, which is the algorithm every date
/// library uses underneath: it shifts the year so that March is the first
/// month, which makes the leap day the last day of the year and removes every
/// special case from the arithmetic. Written out rather than depended on
/// because it is the only calendar fact this crate needs.
///
/// Returns `None` for a day outside the month, so a malformed date cannot
/// become a valid-looking timestamp.
fn days_from_civil(year: i64, month: i64, day: i64) -> Option<i64> {
    if day < 1 || day > days_in_month(year, month)? {
        return None;
    }

    let year = if month <= 2 { year - 1 } else { year };
    let era = if year >= 0 { year } else { year - 399 } / 400;
    let year_of_era = year - era * 400;
    let day_of_year = (153 * (if month > 2 { month - 3 } else { month + 9 }) + 2) / 5 + day - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;

    Some(era * 146_097 + day_of_era - 719_468)
}

/// How many days `month` has in `year`, or `None` when the month is not one of
/// the twelve.
fn days_in_month(year: i64, month: i64) -> Option<i64> {
    let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    match month {
        1 | 3 | 5 | 7 | 8 | 10 | 12 => Some(31),
        4 | 6 | 9 | 11 => Some(30),
        2 if leap => Some(29),
        2 => Some(28),
        _ => None,
    }
}

#[cfg(test)]
#[path = "../tests/ssl/certificate_expiry_tests.rs"]
mod tests;
