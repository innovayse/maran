//! Reading the source's database listing.

use std::collections::HashSet;

use super::model::line_window::LineWindow;
use super::model::listed_database::ListedDatabase;
use super::model::parsed::Parsed;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;

/// MySQL's identifier ceiling, in bytes.
const MAX_NAME_BYTES: usize = 64;

/// The server's own schemas, which are never a customer's. The remote command
/// already leaves them out; a listing that carries one anyway has it ignored
/// here, so the parser stands on its own.
const SYSTEM_SCHEMAS: [&str; 4] = ["information_schema", "mysql", "performance_schema", "sys"];

/// Reads the output of `ListDatabases` (`<name>\t<size bytes>` per line) into
/// database names and sizes.
///
/// A name must be non-empty, at most 64 bytes and made of ASCII letters, digits,
/// `_`, `$` and `-` — the shape of an identifier that can be handed back to the
/// source and displayed; anything else is skipped with a `NameRefused` warning
/// that carries no text. Whether a name belongs to an account, and whether it
/// passes the database-name validator for that account, is decided when it is
/// attributed (`assign_databases_to_accounts`), because that validator is
/// account-relative. The size is the second tab-separated field: digits, with an
/// all-zero fractional part tolerated (a `DECIMAL` sum prints one); `NULL`, empty,
/// negative or unreadable is "not measured". A repeated name keeps its first line.
///
/// Bounded through [`LineWindow`]: the line bound is the bound on databases, and
/// a read it cut short sends `Truncated`. Never fails.
#[must_use]
pub fn parse_mysql_listing(listing: &str) -> Parsed<ListedDatabase> {
    let window = LineWindow::of(listing);
    let mut parsed = Parsed::new();
    let mut seen: HashSet<&str> = HashSet::new();
    let mut refused = window.rejected;
    for &line in &window.lines {
        if line.trim().is_empty() {
            continue;
        }
        let (name, size_text) = line.split_once('\t').unwrap_or((line, ""));
        if SYSTEM_SCHEMAS.contains(&name) {
            continue;
        }
        if !is_database_name(name) {
            refused += 1;
            continue;
        }
        if !seen.insert(name) {
            continue;
        }
        parsed.items.push(ListedDatabase {
            name: name.to_owned(),
            size_bytes: size_of(size_text),
        });
    }
    for _ in 0..refused {
        parsed.warnings.push(SourceWarningRecord::about_nothing(
            SourceWarningKind::NameRefused,
        ));
    }
    if window.truncated {
        parsed.warnings.push(SourceWarningRecord::about_nothing(
            SourceWarningKind::Truncated,
        ));
    }
    parsed
}

/// Whether `name` has the shape of a database name.
fn is_database_name(name: &str) -> bool {
    !name.is_empty()
        && name.len() <= MAX_NAME_BYTES
        && name
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'_' | b'$' | b'-'))
}

/// The size a field states, or `None` when it states none.
fn size_of(field: &str) -> Option<u64> {
    let text = field.trim();
    let whole = match text.split_once('.') {
        Some((whole, fraction)) if fraction.bytes().all(|byte| byte == b'0') => whole,
        Some(_) => return None,
        None => text,
    };
    if whole.is_empty() || !whole.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }
    whole.parse().ok()
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_mysql_listing_tests.rs"]
mod tests;
