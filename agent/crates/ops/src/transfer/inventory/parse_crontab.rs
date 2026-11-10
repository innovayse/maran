//! Counting the entries of one crontab.

use super::model::crontab_reading::CrontabReading;
use super::model::line_window::LineWindow;

/// The special schedules a crontab line may start with.
const SPECIAL_SCHEDULES: [&str; 9] = [
    "@reboot",
    "@yearly",
    "@annually",
    "@monthly",
    "@weekly",
    "@daily",
    "@midnight",
    "@hourly",
    "@every",
];

/// The month and weekday names a schedule field may use.
const NAMES: [&str; 19] = [
    "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec", "sun",
    "mon", "tue", "wed", "thu", "fri", "sat",
];

/// Counts the scheduled entries of the output of `crontab -l`.
///
/// Blank lines, comments and environment settings (`NAME=value`) are ignored. An
/// entry is either a special schedule (`@daily`, ...) followed by a command, or
/// five schedule fields followed by a command. A line that is none of these, an
/// over-long line, or one with a NUL byte is counted in `skipped_lines` and never
/// counted as an entry. Only the number is kept: the command text is not read
/// into the inventory.
///
/// Bounded through [`LineWindow`]; a bound reached sets `truncated`. Never fails.
#[must_use]
pub fn parse_crontab(listing: &str) -> CrontabReading {
    let window = LineWindow::of(listing);
    let mut entries = 0u32;
    let mut skipped = u32::try_from(window.rejected).unwrap_or(u32::MAX);
    for line in &window.lines {
        let text = line.trim();
        if text.is_empty() || text.starts_with('#') || is_environment_setting(text) {
            continue;
        }
        if is_entry(text) {
            entries = entries.saturating_add(1);
        } else {
            skipped = skipped.saturating_add(1);
        }
    }
    CrontabReading {
        entry_count: entries,
        skipped_lines: skipped,
        truncated: window.truncated,
    }
}

/// Whether `text` is `NAME=value` with `NAME` an identifier.
fn is_environment_setting(text: &str) -> bool {
    let Some((name, _)) = text.split_once('=') else {
        return false;
    };
    let name = name.trim();
    let mut characters = name.chars();
    characters
        .next()
        .is_some_and(|first| first.is_ascii_alphabetic() || first == '_')
        && characters.all(|c| c.is_ascii_alphanumeric() || c == '_')
}

/// Whether `text` is a scheduled entry.
fn is_entry(text: &str) -> bool {
    let words: Vec<&str> = text.split_whitespace().collect();
    match words.first() {
        Some(first) if first.starts_with('@') => {
            words.len() >= 2 && SPECIAL_SCHEDULES.contains(first)
        }
        Some(_) => words.len() >= 6 && words.iter().take(5).all(|field| is_schedule_field(field)),
        None => false,
    }
}

/// Whether `field` is a schedule field: lists (`,`), ranges (`-`) and steps (`/`)
/// of `*`, `?`, numbers and the three-letter month and weekday names.
fn is_schedule_field(field: &str) -> bool {
    field.split([',', '-', '/']).all(|part| {
        part == "*"
            || part == "?"
            || (!part.is_empty() && part.bytes().all(|byte| byte.is_ascii_digit()))
            || NAMES.contains(&part.to_ascii_lowercase().as_str())
    })
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_crontab_tests.rs"]
mod tests;
