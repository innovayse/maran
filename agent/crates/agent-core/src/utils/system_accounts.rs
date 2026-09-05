//! Reading the host's local password database into rows.

use crate::utils::system_account::SystemAccount;

/// The field separator of the local password database.
const SEPARATOR: char = ':';

/// Field index of the login name.
const NAME_FIELD: usize = 0;

/// Field index of the numeric user id.
const UID_FIELD: usize = 2;

/// Field index of the numeric primary group id.
const GID_FIELD: usize = 3;

/// Field index of the home directory.
///
/// The file's format is `name:password:uid:gid:gecos:home:shell`, counted from
/// the front rather than from the end because the shell that follows the home
/// may itself be absent on a truncated line.
const HOME_FIELD: usize = 5;

/// How many fields a row must have before any of the four are believed.
///
/// Six, not seven: a row whose trailing shell field is missing still names an
/// account correctly, and refusing it would lose a real account over a field
/// nothing here reads.
const MINIMUM_FIELDS: usize = 6;

/// Parses the text of a local password database into one row per account.
///
/// A pure function over the file's text, with no filesystem access of its own,
/// so the caller decides what it read and a test decides what it parsed. It
/// answers a question about the HOST — which accounts exist on this machine —
/// and carries no knowledge of any feature, which is what puts it here rather
/// than beside one of its callers.
///
/// **The local file only, never the name service.** This is a parse of
/// `/etc/passwd`-shaped text and not a `getpwent` walk, and the narrowing is
/// deliberate: enumerating through libc means holding iterator state across a
/// root process's threads, while every account this panel creates is a local
/// entry in that one file. What is given up is visibility of accounts served by
/// LDAP or another name service — which this panel never creates, and which it
/// must not delete or bill for.
///
/// Rows that cannot be read as an account are skipped rather than failing the
/// whole file: a comment, a blank line, an NIS compatibility entry (`+`), a
/// line truncated by a partial write, or a `uid`/`gid` that is not a number.
///
/// That last one is STRICTER than the `ProcessSftpHost` method body this was
/// extracted from, which read the name and the home and never looked at the
/// numeric fields at all. The difference is deliberate, and the reason is
/// **well-formedness, not need**: no caller reads `uid` or `gid`. The SFTP area
/// matches on the name and the home and resolves identity through
/// `AccountIds::resolve`; the monitoring area matches on the name and the home
/// and measures the tree. The parse is a CHECK ON THE LINE, not a value anybody
/// consumes — a row whose third field is not a `u32` is not a passwd line, and
/// the name and home read out of it are worth no more than the uid was. So the
/// two fields are parsed to find out whether the line is one this agent should
/// believe at all, and their values are then carried because a row that dropped
/// them would be a shape nobody could extend.
///
/// Stated without dressing it up: this is a **conservative refusal with no
/// specific downstream harm behind it**. Nothing today would misbehave on a row
/// with `uid=oops`; it would simply act on a line that is corrupt in a field it
/// happens not to read. The cost is real and is not hidden — such a row is now
/// invisible to the SFTP area's login enumeration, so a login carrying one
/// would survive its account's deletion, and an operator would find it by hand.
/// That cost is accepted because the precondition is a passwd file no tool on
/// the host wrote, and because "the line parses as a passwd line" is a cheaper
/// thing to guarantee here than at each of two call sites.
/// Every caller is answering "which accounts are on this host", and refusing to
/// answer at all because one line was malformed is worse than answering about
/// the lines that were not. The rows come back in the file's own order; a
/// caller that needs a stable order sorts.
#[must_use]
pub fn system_accounts(passwd: &str) -> Vec<SystemAccount> {
    passwd
        .lines()
        .filter_map(|line| {
            let fields: Vec<&str> = line.split(SEPARATOR).collect();
            if fields.len() < MINIMUM_FIELDS {
                return None;
            }

            let name = fields.get(NAME_FIELD)?;
            if name.is_empty() {
                return None;
            }

            Some(SystemAccount {
                name: (*name).to_owned(),
                uid: fields.get(UID_FIELD)?.parse().ok()?,
                gid: fields.get(GID_FIELD)?.parse().ok()?,
                home: (*fields.get(HOME_FIELD)?).to_owned(),
            })
        })
        .collect()
}

#[cfg(test)]
#[path = "../tests/utils/system_accounts_tests.rs"]
mod tests;
