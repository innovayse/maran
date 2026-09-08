//! The pre-scan: reading an archive's member list and refusing the archive
//! before anything at all is unpacked.

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;

/// The archive's manifest member. Part of the fixed layout (R2).
const MANIFEST_MEMBER: &str = "manifest.json";

/// The two directory prefixes the layout allows, each with its separator.
const DIRECTORY_PREFIXES: [&str; 2] = ["home/", "databases/"];

/// The two directory members themselves, as `tar` stores the directories.
const DIRECTORY_MEMBERS: [&str; 2] = ["home", "databases"];

/// The path component that walks upwards.
const PARENT_COMPONENT: &str = "..";

/// How much of a refused member name is kept for the operator's message.
///
/// A name is attacker-chosen and `tar` allows very long ones; a message is a
/// log line. Enough to recognise the member, not enough to be a payload.
const NAME_BUDGET: usize = 96;

/// Renders a name that came out of an archive so it can go in an error.
///
/// Every byte outside printable ASCII becomes an escape, and the result is
/// truncated to [`NAME_BUDGET`]. This is not cosmetic: the name is
/// attacker-chosen text about to reach an operator's log and the panel's audit
/// record, and an embedded newline there splits one entry into two — the same
/// mechanism rules/security.md item 4 forbids for crontab and nginx lines. The
/// value is escaped at the point it enters a typed error rather than at each of
/// the places that later render one, because only the first of those is a
/// single place.
///
/// Truncation is silent by design: a marker would itself be text an archive
/// could imitate, and the message is an identifier for a human, not a document.
pub(crate) fn refused_member_name(name: &str) -> String {
    name.chars()
        .flat_map(char::escape_default)
        .take(NAME_BUDGET)
        .collect()
}

/// Reads `artifact`'s member list and refuses an archive whose layout is not
/// the one this agent writes.
///
/// # Why this runs before `tar` is asked to extract anything
///
/// The archive is customer-supplied bytes that root is about to unpack. There
/// are two mechanisms standing between it and `/etc/cron.d`, and they are not
/// the same mechanism twice:
///
/// - **This scan is the brace.** It reads names only, decides on the WHOLE
///   archive, and refuses it entire. An archive holding one member outside the
///   layout is an archive this agent does not understand, so nothing in it is
///   unpacked — not even the members that looked fine. That is the difference
///   between refusing and skipping: a skip unpacks the rest and reports
///   success, which is how a hostile member's neighbours get written anyway.
/// - **`tar`'s own stripping is the belt behind it.** Without
///   `--absolute-names` (which [`crate::backup::model::extract_spec::ExtractSpec`]
///   has no field to set), `tar` strips a leading `/` and refuses a member with
///   a `..` component. That is a per-member defence built by somebody else, and
///   it is the reason this scan is not the only thing between a hostile archive
///   and the disk — not the reason it can be relaxed.
///
/// The scan is the one that decides, so it is the one whose failure is a typed
/// refusal an operator can read, and the one a named test mutates.
///
/// # What it refuses
///
/// A member is accepted only if it is `manifest.json`, is `home` or
/// `databases` themselves, or begins with `home/` or `databases/`. Anything
/// else is [`BackupError::UnexpectedArchiveMember`]. Two properties follow, and
/// only one of them is free:
///
/// - **An absolute member is refused by that rule alone**, because `/anything`
///   does not begin with either prefix. There is deliberately no second,
///   separate check for a leading `/`: it could never fail on its own, and a
///   check that cannot fail alone is decoration that the next reader mistakes
///   for protection.
/// - **A `..` component is NOT covered by it**, because `home/../../etc/passwd`
///   begins with `home/` and passes the prefix rule cleanly. So it gets its own
///   check, and that check earns its place — it is the only thing in this
///   function that refuses that member.
///
/// # Errors
///
/// Returns [`BackupError::ArchiveFailed`] when the archive's members cannot be
/// listed at all, and [`BackupError::UnexpectedArchiveMember`] naming the first
/// member outside the layout.
pub(crate) fn scan_members(
    host: &dyn BackupHost,
    artifact: &std::path::Path,
) -> Result<Vec<String>, BackupError> {
    let names = host.list_members(artifact)?;
    for name in &names {
        refuse_unless_placeable(name)?;
    }

    Ok(names)
}

/// Refuses one member name that the archive's layout has no place for.
///
/// # Errors
///
/// [`BackupError::UnexpectedArchiveMember`].
fn refuse_unless_placeable(name: &str) -> Result<(), BackupError> {
    // `tar` lists a directory with a trailing separator; the layout is about
    // the path, not about which kind of entry wears it.
    let trimmed = name.trim_end_matches('/');

    let placeable = trimmed == MANIFEST_MEMBER
        || DIRECTORY_MEMBERS.contains(&trimmed)
        || DIRECTORY_PREFIXES
            .iter()
            .any(|prefix| trimmed.starts_with(prefix));
    let walks_up = trimmed
        .split('/')
        .any(|component| component == PARENT_COMPONENT);

    if !placeable || walks_up {
        return Err(BackupError::UnexpectedArchiveMember {
            name: refused_member_name(name),
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/backup/archive/scan_members_tests.rs"]
mod tests;
