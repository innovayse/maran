//! The one line cron actually runs, and the reason every byte of it is ours.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::model::cron_entry::CronEntry;

/// What a disabled entry's line is prefixed with.
///
/// The `#` is what stops cron: the line becomes a comment, so the schedule is
/// never read and the entry cannot run. The `off#` after it makes the prefix
/// ours rather than an ordinary comment, so re-enabling can strip exactly this
/// and a customer's own `# ` comment is never mistaken for a disabled entry.
///
/// The trailing space matters and is part of the constant: stripping the prefix
/// must leave the schedule starting at its first field. It is `pub(crate)`
/// because [`CrontabDocument`](super::model::crontab_document::CrontabDocument)
/// strips on the way in what this file writes on the way out, and two spellings
/// of one prefix is an entry that can be disabled and never re-enabled.
pub(crate) const DISABLED_PREFIX: &str = "#off# ";

/// Renders the line cron reads for `entry`.
///
/// **Every byte of it is agent-derived, and that is the point of this whole
/// area.** The schedule renders from five validated fields that cannot hold a
/// space or a control character; the id is a uuid this agent minted; the three
/// paths are built by [`AgentPaths`] from the account and that id; and
/// everything between them is a constant in this file. The customer's command
/// is not here at all — it is the contents of the `.cmd` file this line names.
///
/// Two earlier designs put the command on this line and both were disproved on
/// a real host: cron rewrites the first unescaped `%` into a newline, and
/// `echo hi # comment` parses standalone but not inside a `( … )` wrapper.
/// There is no wrapper here and no `%` anywhere in the constant text, so
/// neither has anything to act on.
///
/// `> ` and not `>> `: the panel shows the LAST run, so the log is truncated on
/// every run and cannot grow without bound inside a customer's home.
///
/// `echo $? > <exit>` and not a `date` call: the file's own modification time
/// is when the run finished, so the timestamp costs no second command — and no
/// `%`, which a `date +%s` would have needed.
pub(crate) fn installed_line(entry: &CronEntry, account: &AccountName, sh_binary: &str) -> String {
    let command_file = AgentPaths::cron_cmd_path(account, &entry.id);
    let log_file = AgentPaths::cron_log_path(account, &entry.id);
    let exit_file = AgentPaths::cron_exit_path(account, &entry.id);
    let prefix = if entry.enabled { "" } else { DISABLED_PREFIX };

    format!(
        "{prefix}{schedule} {sh_binary} {command} > {log} 2>&1; echo $? > {exit}",
        schedule = entry.schedule,
        command = command_file.display(),
        log = log_file.display(),
        exit = exit_file.display(),
    )
}
