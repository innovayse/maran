//! The six tables the local-submission binding is rendered into (issue #518).

use std::path::Path;
use std::sync::{Mutex, MutexGuard, PoisonError};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::local_submission_guard::assert_rule_loaded;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::local_submission_key::LocalSubmissionKey;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_unit::MailUnit;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};

/// The mode a table lands at: Rspamd and Postfix read it as their own users.
const TABLE_MODE: u32 = 0o644;

/// The lines one account owns in each table, already rendered.
///
/// An empty vector means "the account has no line there". Removal passes all
/// six empty.
#[derive(Debug, Default)]
pub(crate) struct AccountLines {
    /// `<uid> <account>` lines of `local_submit_accounts.map`.
    pub(crate) local_accounts: Vec<String>,
    /// `<account> <domain>` lines of `account_sender_domains.map`.
    pub(crate) sender_domains: Vec<String>,
    /// The `<account> <limit>` line of `account_send_limits.map`.
    pub(crate) send_limits: Vec<String>,
    /// `<login> <account>` lines of `mailbox_accounts.map`.
    pub(crate) mailbox_accounts: Vec<String>,
    /// The `<account> OK` line of Postfix's `maran_local_submit_users`.
    pub(crate) submit_users: Vec<String>,
    /// `<alias> <external target count> <account>` lines of `alias_fanout.map`.
    pub(crate) alias_fanouts: Vec<String>,
}

/// What an empty table holds: a comment line, never zero bytes. Rspamd feeds a
/// callback map only from a file with content, so a zero-byte table (the last
/// account removed) would leave the filter holding the previous, populated copy.
/// A comment line is skipped by every reader (the rule's parser, Postfix's table).
pub(crate) const EMPTY_TABLE: &str = "# no entries\n";

/// The one lock every read-render-write of these tables runs under.
///
/// Two RPCs are served concurrently, and each reads the tables, edits ONE
/// account's lines and writes the whole file back. Unserialised, the second
/// writer renders from a stale read and puts back a line the first removed: a
/// suspension undone by a domain change. `config_tree_lock` cannot be that lock:
/// it is taken inside every `safe_write` entry point and is not re-entrant, so
/// holding it across the read and the writes would deadlock them. This mutex is
/// taken OUTSIDE it, always in that order (tables, then config tree) and never the
/// reverse, so the two cannot deadlock; it makes the read and every write of the
/// change one critical section, and it is the only way these tables are modified.
static TABLES: Mutex<()> = Mutex::new(());

/// Takes [`TABLES`]. A holder that panicked left the tables as the config-write
/// protocol left them (renamed whole or restored), so a poisoned lock is still
/// safe to take.
pub(crate) fn hold_tables() -> MutexGuard<'static, ()> {
    TABLES.lock().unwrap_or_else(PoisonError::into_inner)
}

/// Whether some thread holds [`TABLES`] right now. A test double calls this from inside a host method to
/// learn whether the change that reached it runs under the lock, without a thread, a sleep or a race.
#[cfg(test)]
pub(crate) fn tables_are_held() -> bool {
    matches!(TABLES.try_lock(), Err(std::sync::TryLockError::WouldBlock))
}

/// How a table treats a key that another account already holds.
#[derive(Debug, Clone, Copy)]
enum KeyRule {
    /// The table's key is the account itself: nothing to collide.
    Account,
    /// The key is a uid, and uids are handed out again: the account bound last
    /// wins and any other account's line with that uid is dropped, so the older
    /// account is unbound (its mail is refused) rather than answering for the new one.
    NewestWins { key_column: usize },
    /// The key is a login or a domain that names ONE account: another account's
    /// line is a conflict, refused before anything is written.
    Exclusive {
        key_column: usize,
        key: LocalSubmissionKey,
    },
}

/// One table, its rendered additions and how its keys are owned.
struct Table<'a> {
    path: &'static str,
    owner_column: usize,
    rule: KeyRule,
    added: &'a [String],
}

/// The non-blank, non-comment lines of a table.
fn data_lines(text: &str) -> impl Iterator<Item = &str> {
    text.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
}

/// Sorted, de-duplicated lines as table text; the comment line for no lines.
fn render_lines(mut lines: Vec<String>) -> String {
    lines.sort();
    lines.dedup();
    if lines.is_empty() {
        return EMPTY_TABLE.to_owned();
    }
    let mut rendered = lines.join("\n");
    rendered.push('\n');
    rendered
}

/// Returns `current` with every line owned by `account` dropped and `added`
/// put in, sorted so a repeated push renders the same bytes.
///
/// `account_column` is the whitespace-separated field that names the owner: the
/// first for the tables keyed by account, the second for the two keyed by uid or
/// login. Lines of other accounts are kept verbatim. A table with no line left is
/// [`EMPTY_TABLE`].
pub(crate) fn replace_account_lines(
    current: &str,
    account_column: usize,
    account: &str,
    added: &[String],
) -> String {
    let mut lines: Vec<String> = data_lines(current)
        .filter(|line| line.split_whitespace().nth(account_column) != Some(account))
        .map(str::to_owned)
        .collect();
    lines.extend(added.iter().cloned());
    render_lines(lines)
}

/// The keys `added` puts in, read from `key_column`.
fn added_keys(added: &[String], key_column: usize) -> Vec<&str> {
    added
        .iter()
        .filter_map(|line| line.split_whitespace().nth(key_column))
        .collect()
}

/// Renders `table` for `account` from its current text, or names the key another
/// account holds.
fn render_table(current: &str, table: &Table<'_>, account: &str) -> Result<String, MailError> {
    match table.rule {
        KeyRule::Account => Ok(replace_account_lines(
            current,
            table.owner_column,
            account,
            table.added,
        )),
        KeyRule::NewestWins { key_column } => {
            let keys = added_keys(table.added, key_column);
            let kept: Vec<String> = data_lines(current)
                .filter(|line| {
                    line.split_whitespace()
                        .nth(key_column)
                        .is_none_or(|key| !keys.contains(&key))
                })
                .map(str::to_owned)
                .collect();
            Ok(replace_account_lines(
                &render_lines(kept),
                table.owner_column,
                account,
                table.added,
            ))
        }
        KeyRule::Exclusive { key_column, key } => {
            let keys = added_keys(table.added, key_column);
            for line in data_lines(current) {
                let mut fields = line.split_whitespace();
                let held = fields.clone().nth(key_column);
                let owner = fields.nth(table.owner_column);
                if let (Some(held), Some(owner)) = (held, owner)
                    && owner != account
                    && keys.iter().any(|wanted| wanted.eq_ignore_ascii_case(held))
                {
                    return Err(MailError::LocalSubmissionConflict {
                        key,
                        value: held.to_owned(),
                    });
                }
            }
            Ok(replace_account_lines(
                current,
                table.owner_column,
                account,
                table.added,
            ))
        }
    }
}

/// Replaces `account`'s entries in all six tables.
///
/// The whole change, reads included, runs under [`hold_tables`]: every table is
/// read and rendered first (a key conflict is found here, before any write), then
/// written. The Rspamd maps change as ONE config-write set (validated with
/// `rspamadm configtest`, then Rspamd reloaded, everything restored on refusal);
/// Postfix's table is a second change, indexed by `postmap`. The order is the
/// safe one in both directions: when `grant` the filter's rules and limit are
/// live, and the rule is shown to be loaded, BEFORE `postdrop` admits the user,
/// and when revoking `postdrop` stops admitting the user BEFORE the rules go.
///
/// # Errors
///
/// As [`MailHost::write_config_set`] and [`MailHost::write_file_unreloaded`],
/// plus [`MailError::ConfigUnreadable`] when a table exists and cannot be read,
/// [`MailError::LocalSubmissionConflict`] when a login or domain belongs to another
/// account, and (granting) [`MailError::UnsafeLocalSubmission`] when Rspamd does
/// not report the rule loaded.
pub(crate) fn apply_account_lines(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    account: &str,
    lines: &AccountLines,
    grant: bool,
) -> Result<(), MailError> {
    let _tables = hold_tables();
    let rspamd = render_rspamd_maps(host, account, lines)?;
    let postfix = render_postfix_users(host, distro, account, lines)?;
    if grant {
        write_rspamd_maps(host, distro, &rspamd)?;
        assert_rule_loaded(host, distro)?;
        write_postfix_users(host, distro, &postfix)
    } else {
        write_postfix_users(host, distro, &postfix)?;
        write_rspamd_maps(host, distro, &rspamd)
    }
}

/// Reads and renders the five Rspamd maps.
fn render_rspamd_maps(
    host: &dyn MailHost,
    account: &str,
    lines: &AccountLines,
) -> Result<Vec<(&'static str, String)>, MailError> {
    let tables = [
        Table {
            path: AgentPaths::MAIL_LOCAL_SUBMIT_ACCOUNTS_PATH,
            owner_column: 1,
            rule: KeyRule::NewestWins { key_column: 0 },
            added: &lines.local_accounts,
        },
        Table {
            path: AgentPaths::MAIL_ACCOUNT_SENDER_DOMAINS_PATH,
            owner_column: 0,
            rule: KeyRule::Exclusive {
                key_column: 1,
                key: LocalSubmissionKey::SenderDomain,
            },
            added: &lines.sender_domains,
        },
        Table {
            path: AgentPaths::MAIL_ACCOUNT_SEND_LIMITS_PATH,
            owner_column: 0,
            rule: KeyRule::Account,
            added: &lines.send_limits,
        },
        Table {
            path: AgentPaths::MAIL_MAILBOX_ACCOUNTS_PATH,
            owner_column: 1,
            rule: KeyRule::Exclusive {
                key_column: 0,
                key: LocalSubmissionKey::MailboxLogin,
            },
            added: &lines.mailbox_accounts,
        },
        Table {
            path: AgentPaths::MAIL_ALIAS_FANOUT_PATH,
            owner_column: 2,
            rule: KeyRule::Account,
            added: &lines.alias_fanouts,
        },
    ];
    let mut rendered = Vec::with_capacity(tables.len());
    for table in &tables {
        let current = host.read_config(Path::new(table.path))?.unwrap_or_default();
        rendered.push((table.path, render_table(&current, table, account)?));
    }
    Ok(rendered)
}

/// Reads and renders Postfix's table of local submitters.
fn render_postfix_users(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    account: &str,
    lines: &AccountLines,
) -> Result<String, MailError> {
    let path = AgentPaths::mail_local_submit_users_path(distro.postfix_config_directory());
    let current = host.read_config(&path)?.unwrap_or_default();
    Ok(replace_account_lines(
        &current,
        0,
        account,
        &lines.submit_users,
    ))
}

/// Writes the five rendered Rspamd maps as one change and reloads Rspamd.
fn write_rspamd_maps(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    rendered: &[(&'static str, String)],
) -> Result<(), MailError> {
    let files: Vec<ConfigFile<'_>> = rendered
        .iter()
        .map(|(path, contents)| ConfigFile {
            target: Path::new(path),
            contents,
            mode: TABLE_MODE,
        })
        .collect();
    let unit = distro.rspamd_service();
    let reload = MailUnit::new(unit).reload_or_restart();
    host.write_config_set(
        MailDaemon::Rspamd,
        unit,
        &files,
        &Validator {
            program: &distro.rspamadm_binary(),
            arguments: &["configtest"],
            class: CallClass::Reload,
        },
        &Reload {
            program: &distro.service_manager(),
            arguments: &reload,
        },
    )
}

/// Writes Postfix's rendered table and builds its index.
fn write_postfix_users(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    contents: &str,
) -> Result<(), MailError> {
    let path = AgentPaths::mail_local_submit_users_path(distro.postfix_config_directory());
    let index = format!("{}:{}", distro.postfix_map_type(), path.to_string_lossy());
    host.write_file_unreloaded(
        MailDaemon::Postfix,
        distro.postfix_service(),
        &ConfigFile {
            target: &path,
            contents,
            mode: TABLE_MODE,
        },
        &Validator {
            program: &distro.postmap_binary(),
            arguments: &[index.as_str()],
            class: CallClass::Mutation,
        },
    )
}

#[cfg(test)]
#[path = "../tests/mail/local_submission_tables_tests.rs"]
mod tests;
