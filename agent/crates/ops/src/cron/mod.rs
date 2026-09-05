//! Per-account crontabs: scheduled entries the panel owns, in a file the
//! account and the host also write.
//!
//! Three things shape everything in this area.
//!
//! **The customer's command never appears in the crontab.** It is written
//! verbatim to `~/.maran/cron/<id>.cmd`, and the installed line NAMES that
//! file:
//!
//! ```text
//! <schedule> /bin/sh <home>/.maran/cron/<id>.cmd > <…>.log 2>&1; echo $? > <…>.exit
//! ```
//!
//! Every byte of that line is agent-derived — the schedule from five validated
//! fields, the id from a uuid this agent minted, the three paths from
//! `AgentPaths`, the rest a constant. Two earlier designs put the command on
//! the line and both were disproved on a real host: cron rewrites the first
//! unescaped `%` into a newline, and `echo hi # comment` parses standalone but
//! not inside a `( … )` wrapper. There is no wrapper here and no `%` anywhere
//! in the constant text, so the test that walks the rendered line and asks
//! where each byte came from has nothing to exempt. That is the design, not a
//! by-product of it.
//!
//! It is also why the command's own alphabet is short: `%` and `#` are LEGAL in
//! a [`CronCommand`](maran_agent_core::validation::system::cron_command::CronCommand),
//! because they are ordinary shell text in a file. What is refused there is
//! what would stop the value being one line at all.
//!
//! **The crontab is not this agent's file.** An account with shell access can
//! run `crontab -e`, a host's packaging can seed a table, and an administrator
//! can add a line by hand. So [`CrontabDocument::parse`] cannot fail: a line it
//! does not recognise is carried across byte for byte, in its original
//! position — position, because a cron environment assignment applies to the
//! lines beneath it, and relocating a foreign `PATH=` would change which
//! foreign entries it governs. The agent's own region starts at a banner and is
//! rewritten whole on every install, with `MAILTO=""` and `SHELL` re-emitted
//! below every foreign line so that a managed entry runs under the interpreter
//! this agent chose whatever a hand-edited preamble said.
//!
//! **"Foreign" means "above the banner", and the law is exactly that narrow.**
//! Three consequences follow, each defensible, each with a test of its own so
//! that none of them is only a paragraph: a line an administrator appends BELOW
//! the banner is deleted rather than moved (carrying it up would put it above
//! our environment block, which for an assignment changes what it governs); a
//! forged `# maran-entry:` marker above a foreign job makes the render rewrite
//! that job, because `parse` believes any syntactically valid uuid and not only
//! one this agent minted; and a crontab written with `\r\n` endings is
//! recognised, because otherwise the whole managed region would orphan into
//! foreign text while its entries kept firing.
//!
//! Every operation renders the WHOLE document and hands it to `crontab(1)`.
//! Nothing edits a line in place: the program installs a table or it does not,
//! and rebuilding every managed line from validated values means a line
//! somebody tampered with is repaired by the next install rather than carried
//! forward.
//!
//! **The privilege split runs through the host trait.** `crontab(1)` writes the
//! spool as root, because where that spool lives and how the daemon learns it
//! changed are the program's business on each family. Everything under the
//! account's home is done as the account — the directory is `0700` and the
//! account owns it, so a root process writing through a name inside it would
//! write wherever a planted symlink pointed. [`ProcessCronHost`] carries the
//! full argument, including the one place the drop is not available and what
//! stands in for it there.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`CronHost`]), one implementation that really touches the machine
//! ([`ProcessCronHost`]), one error enum ([`CronError`]) that structurally
//! cannot carry a tool's output, and `model/` for the parsed document and the
//! typed inputs and outputs.
//!
//! [`ProcessCronHost`] itself is thin, because the machine it touches has four
//! separable parts and each is its own file: `crontab_spool` runs `crontab(1)`
//! as root, `entry_files` reads the three files an entry owns over
//! `open_cron_directory`'s per-component descent, `mint_entry_id` mints the id
//! those files are named after, and `installed_line` renders the one line cron
//! actually runs. The host is what forks for the two writes, and delegates
//! everything else.

mod create_cron_entry;
mod cron_error;
mod cron_host;
// Private: the host's cron spool, reached through `crontab(1)`. The two
// operations in this area that run as root, and nothing else.
mod crontab_spool;
mod delete_cron_entry;
// Private: the three hardened root-side reads, with the one set of defences
// they share. `ProcessCronHost` is their only caller, and nothing outside this
// area should be able to start one by another route.
mod entry_files;
mod get_cron_entry_output;
mod get_cron_environment;
// Private: the one line cron runs, and the prefix that hides it from cron.
mod installed_line;
mod list_cron_entries;
// Private: the entry-id minter. An id names three files under a customer's
// home, so nothing outside this area mints one.
mod mint_entry_id;
// Private: the per-component descent into a customer's cron directory. It is
// the containment of every read this area performs, and it is why `O_NOFOLLOW`
// covers every level of the path rather than only the last.
pub mod model;
mod open_cron_directory;
mod process_cron_host;
#[cfg(test)]
#[path = "../tests/cron/recording_cron_host.rs"]
pub(crate) mod recording_cron_host;
mod set_cron_entry_enabled;
mod set_cron_environment;
mod update_cron_entry;

// Crate-visible and not public: the sentence both cron lineages print for an
// account with no table. The ACCOUNTS area reads it too, because deleting an
// account removes its crontab and "there was no crontab" is a success there for
// the same reason it is here. One spelling of it, in the area that owns the
// spool, rather than the same string written down twice.
pub(crate) use crontab_spool::NO_CRONTAB_MARKER;

pub use create_cron_entry::create_cron_entry;
pub use cron_error::CronError;
pub use cron_host::CronHost;
pub use delete_cron_entry::delete_cron_entry;
pub use get_cron_entry_output::get_cron_entry_output;
pub use get_cron_environment::get_cron_environment;
pub use list_cron_entries::list_cron_entries;
pub use model::cron_entry::CronEntry;
pub use model::cron_entry_output::CronEntryOutput;
pub use model::cron_environment::CronEnvironment;
pub use model::cron_run_record::CronRunRecord;
pub use model::crontab_document::CrontabDocument;
pub use process_cron_host::ProcessCronHost;
pub use set_cron_entry_enabled::set_cron_entry_enabled;
pub use set_cron_environment::set_cron_environment;
pub use update_cron_entry::update_cron_entry;
