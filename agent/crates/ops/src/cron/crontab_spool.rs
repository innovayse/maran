//! The host's cron spool, reached the only way this agent reaches it.

use std::fs::{self, OpenOptions, Permissions};
use std::io::Write as _;
use std::os::unix::fs::{OpenOptionsExt as _, PermissionsExt as _};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::utils::spawn_request::SpawnRequest;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::{CronError, PROGRAM_UNAVAILABLE};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// `crontab`'s flag for naming the account whose table is meant.
const USER_FLAG: &str = "-u";

/// `crontab`'s flag for printing the current table.
const LIST_FLAG: &str = "-l";

/// The message both cron lineages print for an account with no table.
///
/// Matched instead of an exit status, and that is deliberate: the status for
/// this condition is a detail of each implementation, while the sentence is the
/// documented one both print. Pinning a number would turn every account that
/// has never had an entry into an error on the first family that chose another.
///
/// **It is looked for in standard ERROR and nowhere else, and that is a
/// security property rather than tidiness.** Standard output carries the
/// account's own crontab — bytes the customer writes — so an account that put
/// `# no crontab for alice` in its table could otherwise make a FAILED
/// `crontab -l` read as "this account has no crontab". Every caller then parses
/// an empty document and the next install writes it back, erasing every entry
/// the account had, foreign lines included. A customer's bytes must not
/// participate in this decision at all, so the answer is taken from the stream
/// they cannot write, and only when the stream they can write is empty.
pub(crate) const NO_CRONTAB_MARKER: &str = "no crontab for";

/// The most bytes of a crontab this agent will read.
///
/// The one account-controlled input in this area that had no ceiling, while
/// every other has an argued one — 8 KiB for a command file, 32 for an exit
/// file, 64 KiB for an output tail. The table is read into the root daemon and
/// then copied several times on its way through `parse` and `render`, so its
/// size is a multiple of itself in the daemon's memory, and rules/rust.md is
/// explicit that a customer-sized read into a `Vec` is a denial of service
/// against the panel.
///
/// A quarter of a mebibyte is roughly a thousand managed entries, or two and a
/// half thousand lines of a hundred characters — far past what any account
/// needs and far short of what would hurt. A table above it is REFUSED rather
/// than truncated: half a crontab parsed and re-installed would delete the
/// other half.
const CRONTAB_CEILING: u64 = 256 * 1024;

/// The mode the root-owned scratch directory is held at.
const SCRATCH_MODE: u32 = 0o700;

/// The mode the crontab handed to `crontab(1)` is written at.
const TABLE_MODE: u32 = 0o600;

/// The `code` reported when the account's table is larger than
/// [`CRONTAB_CEILING`].
///
/// Negative, like [`PROGRAM_UNAVAILABLE`], so it can never collide with an exit
/// status; distinct from it so an operator reading the number can tell "the
/// program never ran" from "the program ran and its answer was too large to
/// take".
const TABLE_TOO_LARGE: i32 = -2;

/// Prefix every temporary crontab this host writes carries.
const TABLE_PREFIX: &str = "crontab";

// The environment and the bounds this file's spawns are given.
//
// Both of this file's spawns go through the bounded core (`run_bounded`), which
// clears the environment the same way every other spawn in the agent does, gives the
// child a process group of its own, a deadline taken from its call class and a ceiling
// on each output stream. Their cron-specific sentinel ([`PROGRAM_UNAVAILABLE`]) stays
// here, in the mapping from the core's error.
//
// The cleared environment matters more here than almost anywhere: the absent-crontab
// answer is decided by matching a MESSAGE ([`NO_CRONTAB_MARKER`]), so the language that
// message is printed in is part of that control decision. Inheriting the daemon's
// environment would make it ambient state — whatever `LANG` the unit file, the installer
// or the operator's shell happened to leave behind — and a control decision must not rest
// on that.
//
// It changes the direction of nothing: an unmatched message was already a refusal rather
// than a wrong "this account has no crontab", so what this removes is a source of
// spurious refusals on a non-English host, not a hole.

/// One account's crontab, read and installed through `crontab(1)`.
///
/// **These are the two operations in this area that run as ROOT**, and that is
/// the correct privilege for them: `crontab(1)` is the proper writer of the
/// spool, because where that spool lives, what owns it, what mode it carries
/// and how the daemon learns it changed are the program's business on each
/// family. Asking the program to do the work is what keeps all four out of
/// `ops`. Nothing here touches a customer's home; the two operations that do
/// fork and drop first.
///
/// **The `-u` argument is always a [`ManagedAccount`]**, never a bare name, so
/// `crontab -u root` and every other system user's table are unreachable from
/// here: the proof was resolved by the caller and is checked before this
/// program is ever run.
///
/// **There is no partial install.** The table replaces what was there or it
/// does not, which is what lets every operation in this area render the WHOLE
/// document and hand it over instead of editing lines in place on a file two
/// writers could be holding.
///
/// The table is staged in [`AgentPaths::agent_scratch_dir`] — root-owned and
/// `0700` — and never anywhere under a customer's home, because a root process
/// writing a temporary file into a directory a customer owns is a symlink the
/// customer plants once and root follows every time afterwards. It is created
/// with `O_EXCL` on top of that, and removed whether the install succeeded or
/// not: it holds the account's whole crontab, including any environment value
/// the customer set, and a copy of that left under `/run` is a copy nobody
/// remembers to look at.
///
/// **Both methods MUST be called from `tokio::task::spawn_blocking`**: they
/// spawn a program and wait for it, which on a runtime worker stalls every
/// other in-flight command (rules/rust.md "Async and blocking"). The wait is
/// bounded: the program is killed with its whole process group at the deadline of
/// its call class, so a hung `crontab` ends a task instead of holding a
/// `cron_lock` for as long as the hang lasts.
pub(crate) struct CrontabSpool {
    /// Where `crontab` lives on this family.
    distro: &'static dyn DistroAdapter,
}

impl CrontabSpool {
    /// The spool as reached on `distro`.
    pub(crate) fn new(distro: &'static dyn DistroAdapter) -> Self {
        Self { distro }
    }

    /// Reads the managed `account`'s table with `crontab -u <account> -l`, bounded.
    ///
    /// The read is bounded at [`CRONTAB_CEILING`] because the answer is an
    /// account's own file, and under the [`CallClass::Query`] deadline; what the
    /// answer MEANS is decided by [`read_outcome`], which a test can drive.
    ///
    /// No `--` here: `-l` ends the argument list, so no operand follows the
    /// options and there is nothing for a guard to protect.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] when the program refuses for a
    /// reason that is not "this account has no table", cannot be run at all, or
    /// answers more than the ceiling, and [`CronError::Bound`] when it outlives
    /// its deadline and is killed.
    pub(crate) fn read(&self, account: &ManagedAccount) -> Result<Option<String>, CronError> {
        let program = self.distro.crontab_binary();
        let outcome = Self::run(
            program.as_path(),
            &read_arguments(account.name().as_str()),
            CallClass::Query,
            CRONTAB_CEILING,
        )?;

        read_outcome(&outcome)
    }

    /// Replaces `account`'s table with `contents`, whole.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] when the table cannot be staged,
    /// when the program cannot be run, or when it refuses the table — with the
    /// program's own exit status in the last case — and [`CronError::Bound`] when
    /// it outlives its [`CallClass::Mutation`] deadline and is killed, in which
    /// case the table may or may not have been replaced.
    pub(crate) fn install(
        &self,
        account: &ManagedAccount,
        contents: &str,
    ) -> Result<(), CronError> {
        let path = Self::write_table(contents)?;

        // `--` before the file: `crontab` reads its options with `getopt` on both lineages
        // (Debian's cron and cronie), which ends option parsing at `--`. The path is
        // absolute and agent-made, so the guard is defence in depth and not a fix.
        let program = self.distro.crontab_binary();
        let path_text = path.display().to_string();
        let outcome = Self::run(
            program.as_path(),
            &install_arguments(account.name().as_str(), &path_text),
            CallClass::Mutation,
            ChildOutput::DEFAULT_CAP,
        );

        // Best effort, and before the outcome is examined so that no early
        // return can skip it.
        let _ = fs::remove_file(&path);

        let outcome = outcome?;
        if outcome.status != 0 {
            return Err(CronError::CrontabRefused {
                code: outcome.status,
            });
        }

        Ok(())
    }

    /// Runs `program` with `arguments` as an argv array and waits for it, bounded.
    ///
    /// No shell, at any point (rules/security.md item 3): the arguments reach
    /// `execve` one by one, so there is no command line for anything to
    /// re-parse. `program` is an absolute path taken from an `AdapterBinary` by
    /// the caller and never a name resolved through `PATH`; it is a `&Path` here
    /// because this body is private to the spool, which is what lets a test point it
    /// at a stand-in without a public constructor for the adapter's type.
    ///
    /// The spawn is the bounded core's. Standard input is `/dev/null`, so a tool
    /// that decides to prompt fails instead of hanging; the child is in a process
    /// group of its own and is killed with it at `class`'s deadline; and each of
    /// its two output streams is read concurrently under `ceiling`, so a child
    /// that floods standard error cannot block on a full pipe and an answer larger
    /// than the ceiling is DETECTED (`ceiling + 1` bytes are the signal) rather
    /// than silently truncated. A table above the ceiling is refused whole,
    /// because half a crontab parsed and re-installed would delete the other half.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] with a `code` of
    /// [`PROGRAM_UNAVAILABLE`] when the program cannot be started, its input or
    /// output cannot be handled, or it cannot be waited for; with a `code` of
    /// [`TABLE_TOO_LARGE`] when it wrote more than `ceiling` bytes to either
    /// stream; and [`CronError::Bound`] when it outlived its deadline. A timeout
    /// is never reported under either code: "the program never ran" and "its
    /// answer was too large" are both false of a program that was running when it
    /// was stopped.
    fn run(
        program: &Path,
        arguments: &[&str],
        class: CallClass,
        ceiling: u64,
    ) -> Result<CommandOutcome, CronError> {
        run_bounded(SpawnRequest {
            program,
            arguments,
            class,
            input: ChildInput::Null,
            output: ChildOutput::Capture {
                stdout_cap: ceiling,
                stderr_cap: ceiling,
            },
        })
        .map_err(spawn_failure)
    }

    /// Writes `contents` into a fresh file in the root-owned scratch directory
    /// and answers its path.
    ///
    /// `create_new` is `O_EXCL`, so a name already taken — by a file, or by a
    /// symlink whether or not it resolves — is refused rather than written
    /// through. The directory is root-owned and `0700`, so nothing but this
    /// daemon can put a name there in the first place; the exclusive create is
    /// the second answer to that question (rules/security.md, defence in
    /// depth).
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] with a `code` of
    /// [`PROGRAM_UNAVAILABLE`] when the directory or the file cannot be made,
    /// or the bytes cannot be flushed.
    fn write_table(contents: &str) -> Result<PathBuf, CronError> {
        let unavailable = || CronError::CrontabRefused {
            code: PROGRAM_UNAVAILABLE,
        };

        let directory = AgentPaths::agent_scratch_dir();
        fs::create_dir_all(directory).map_err(|_| unavailable())?;
        // Set explicitly rather than left to the daemon's umask: the mode is
        // what stops any other user from planting a name in here.
        fs::set_permissions(directory, Permissions::from_mode(SCRATCH_MODE))
            .map_err(|_| unavailable())?;

        let nanoseconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.as_nanos());
        let path = directory.join(format!(
            "{TABLE_PREFIX}-{}-{nanoseconds}",
            std::process::id()
        ));

        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(TABLE_MODE)
            .open(&path)
            .map_err(|_| unavailable())?;
        file.write_all(contents.as_bytes())
            .map_err(|_| unavailable())?;
        file.sync_all().map_err(|_| unavailable())?;

        Ok(path)
    }
}

/// The argument vector of `crontab -u <account> -l`.
///
/// No `--`: `-l` ends the argument list, so no operand follows the options.
fn read_arguments(account: &str) -> [&str; 3] {
    [USER_FLAG, account, LIST_FLAG]
}

/// The argument vector of `crontab -u <account> -- <file>`.
///
/// `--` sits between the options and the file, so the file can never be read as an
/// option; the path is absolute and agent-made, so this is defence in depth.
fn install_arguments<'a>(account: &'a str, file: &'a str) -> [&'a str; 4] {
    [USER_FLAG, account, END_OF_OPTIONS, file]
}

/// Maps a failure of the bounded core onto this area's error.
///
/// A bound on the OUTPUT keeps the code this area already gave an answer that was too
/// large ([`TABLE_TOO_LARGE`]), because a caller and an operator read that number as
/// "the program ran and its answer was too much to take". A bound on TIME is
/// [`CronError::Bound`] and nothing else. Every other failure to run the program is
/// [`PROGRAM_UNAVAILABLE`], the sentinel for "it never got as far as answering".
fn spawn_failure(error: SpawnError) -> CronError {
    match error {
        SpawnError::Bound(BoundExceeded::OutputTooLarge { .. }) => CronError::CrontabRefused {
            code: TABLE_TOO_LARGE,
        },
        SpawnError::Bound(bound) => CronError::Bound(bound),
        _ => CronError::CrontabRefused {
            code: PROGRAM_UNAVAILABLE,
        },
    }
}

/// The bounded spawn, reachable by a test.
///
/// [`CrontabSpool::run`] is an associated function of a type a test cannot
/// usefully build — it would need a real `DistroAdapter` and a real account —
/// while the properties worth testing are properties of the SPAWN itself: which
/// environment the child gets, and whether a child that floods standard error can
/// still exit. It is `#[cfg(test)]` so nothing outside the tests can reach the spawn
/// by another route, and it takes the path, not an `AdapterBinary`, which a test
/// cannot construct.
#[cfg(test)]
fn run_bounded_for_test(
    program: &Path,
    arguments: &[&str],
    ceiling: u64,
) -> Result<CommandOutcome, CronError> {
    CrontabSpool::run(program, arguments, CallClass::Query, ceiling)
}

/// Decides what one run of `crontab -u <account> -l` means.
///
/// Split out of [`CrontabSpool::read`] as a pure function of the outcome, so
/// the decision can be driven by a test: it is the one place in this area where
/// a program's output participates in a control decision, and getting it wrong
/// destroys a customer's crontab rather than failing an operation.
///
/// Three answers, in this order:
///
/// 1. **Exit zero** — standard output is the table, whatever is in it.
/// 2. **Exit non-zero, standard output EMPTY, and standard error carrying the
///    absent-crontab message** — the account has no table. All three conditions
///    are required. The account writes standard output, so the emptiness is not
///    a tidiness check: it is what keeps a customer who put
///    `# no crontab for alice` in their own table from making a failed listing
///    read as "this account has nothing", after which the next install would
///    write an empty document over everything they had.
/// 3. **Anything else** — a refusal, reported with the program's own status.
///
/// The failure this closes is a fail-OPEN one, which is why the emptiness test
/// is written as a requirement on the answer rather than as a search for
/// something bad in it.
///
/// **The direction of the remaining risk is deliberate.** Both lineages print
/// the message on standard error, but that has not yet been observed on the
/// polygon images by this code — and if some family printed it on standard
/// output instead, this would answer `CrontabRefused` for every account that
/// has never had a crontab, so creation would fail loudly and at once. The
/// version this replaced would instead have erased a crontab silently. A
/// refusal that is wrong is recoverable; a deletion that is wrong is not.
///
/// # Errors
///
/// Returns [`CronError::CrontabRefused`] with the program's exit status for
/// every non-zero exit that is not the absent-crontab case.
fn read_outcome(outcome: &CommandOutcome) -> Result<Option<String>, CronError> {
    if outcome.status == 0 {
        return Ok(Some(outcome.stdout.clone()));
    }

    if outcome.stdout.is_empty() && outcome.stderr.contains(NO_CRONTAB_MARKER) {
        return Ok(None);
    }

    Err(CronError::CrontabRefused {
        code: outcome.status,
    })
}

#[cfg(test)]
#[path = "../tests/cron/crontab_spool_tests.rs"]
mod tests;
