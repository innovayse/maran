//! Reading the source's account database into hosting accounts.

use std::collections::HashSet;

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::name::AccountName;

use super::model::inventory_limits::InventoryLimits;
use super::model::line_window::LineWindow;
use super::model::parsed::Parsed;
use super::model::passwd_entry::PasswdEntry;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;

/// The lowest user id of a person or a hosting account on the source.
const FIRST_REGULAR_UID: u32 = 1000;

/// The id of `nobody`, which is above every regular id and is not an account.
const NOBODY_UID: u32 = 65534;

/// The prefix of the directory every conventional hosting account lives under.
const HOME_PREFIX: &str = "/home/";

/// Reads `getent passwd` output and keeps the accounts that can be hosting
/// accounts.
///
/// An entry is kept when its user id is a regular one (1000 up, `nobody`
/// excluded) AND either its shell is a real login shell or its home is under
/// `/home/` (so an SFTP-only account with a `nologin` shell is kept, and a
/// service account with a `nologin` shell and a home elsewhere is not). Its name
/// must pass [`AccountName::parse`] and its home [`RemotePath::parse`]; an entry
/// that fails either is skipped with a `NameRefused` warning, never repaired, and
/// the warning carries no text from the source (the account name only when the
/// name validated and the home did not).
///
/// Bounded and tolerant: lines are read through [`LineWindow`]; a malformed,
/// over-long or NUL-carrying line is counted and reported once; more than
/// [`InventoryLimits::MAX_ACCOUNTS`] accounts stop the read with `Truncated`; a
/// repeated name keeps its first entry. It never panics and never fails.
#[must_use]
pub fn parse_passwd(listing: &str) -> Parsed<PasswdEntry> {
    let window = LineWindow::of(listing);
    let mut parsed = Parsed::new();
    let mut seen: HashSet<&str> = HashSet::new();
    let mut malformed = window.rejected;

    for line in &window.lines {
        if line.trim().is_empty() || line.starts_with('#') {
            continue;
        }
        let fields: Vec<&str> = line.split(':').collect();
        let (Some(&name), Some(&uid_text), Some(&home), Some(&shell)) =
            (fields.first(), fields.get(2), fields.get(5), fields.get(6))
        else {
            malformed += 1;
            continue;
        };
        let Ok(uid) = uid_text.parse::<u32>() else {
            malformed += 1;
            continue;
        };
        if uid < FIRST_REGULAR_UID || uid == NOBODY_UID {
            continue;
        }
        if !(has_login_shell(shell) || home.starts_with(HOME_PREFIX)) {
            continue;
        }
        if !seen.insert(name) {
            continue;
        }
        if parsed.items.len() >= InventoryLimits::MAX_ACCOUNTS {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            return parsed;
        }
        let Ok(account) = AccountName::parse(name) else {
            parsed.warnings.push(SourceWarningRecord::about_nothing(
                SourceWarningKind::NameRefused,
            ));
            continue;
        };
        let Ok(home) = RemotePath::parse(home) else {
            parsed.warnings.push(SourceWarningRecord::about_account(
                SourceWarningKind::NameRefused,
                &account,
            ));
            continue;
        };
        parsed.items.push(PasswdEntry {
            name: account,
            home,
        });
    }

    if malformed > 0 {
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

/// Whether `shell` lets a person log in: not one of the refusing programs. An
/// empty shell field means the system default shell, which does.
fn has_login_shell(shell: &str) -> bool {
    let program = shell.rsplit('/').next().unwrap_or("");
    !matches!(program, "nologin" | "false" | "sync" | "shutdown" | "halt")
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_passwd_tests.rs"]
mod tests;
