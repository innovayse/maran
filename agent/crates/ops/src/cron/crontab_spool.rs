//! The host's cron spool, reached the only way this agent reaches it.

use std::fs::{self, OpenOptions, Permissions};
use std::io::{self, Read as _, Write as _};
use std::os::unix::fs::{OpenOptionsExt as _, PermissionsExt as _};
use std::path::PathBuf;
use std::process::{ChildStderr, Command, Stdio};
use std::thread;
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_argv;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::{CronError, PROGRAM_UNAVAILABLE};

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

/// The locale variable every spawn in this file sets.
///
/// The name is not restated here: it comes from [`spawn_argv`], which is the one
/// place this agent decides what the pin IS. This file's spawns are deliberately
/// not `spawn_argv` — they pipe stdin and carry a cron-specific unavailable
/// sentinel — but they must pin the same variable to the same value, and a
/// second literal is how the two would drift.
const LOCALE_VARIABLE: &str = spawn_argv::LOCALE_VARIABLE;

/// The locale every spawn in this file runs under.
///
/// The absent-crontab answer is decided by matching a MESSAGE
/// ([`NO_CRONTAB_MARKER`]), so the language that message is printed in is part
/// of that control decision. Inheriting the daemon's environment would make it
/// ambient state — whatever `LANG` the unit file, the installer or the
/// operator's shell happened to leave behind — and a control decision must not
/// rest on that. With this set, **both halves of the decision are pinned: the
/// stream the message arrives on, and the language it arrives in.**
///
/// It changes the direction of nothing: an unmatched message was already a
/// refusal rather than a wrong "this account has no crontab", so what this
/// removes is a source of spurious refusals on a non-English host, not a hole.
///
/// The value comes from [`spawn_argv`] for the reason [`LOCALE_VARIABLE`]
/// gives; what is local is this paragraph — the reason cron cannot do without
/// the pin.
const LOCALE_VALUE: &str = spawn_argv::LOCALE_VALUE;

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
/// other in-flight command (rules/rust.md "Async and blocking").
pub(crate) struct CrontabSpool {
    /// Where `crontab` lives on this family.
    distro: &'static dyn DistroAdapter,
}

impl CrontabSpool {
    /// The spool as reached on `distro`.
    pub(crate) fn new(distro: &'static dyn DistroAdapter) -> Self {
        Self { distro }
    }

    /// Reads `account`'s table with `crontab -u <account> -l`, bounded.
    ///
    /// The read is bounded at [`CRONTAB_CEILING`] because the answer is an
    /// account's own file; what the answer MEANS is decided by
    /// [`read_outcome`], which a test can drive.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] when the program refuses for a
    /// reason that is not "this account has no table", cannot be run at all, or
    /// answers more than the ceiling.
    pub(crate) fn read(&self, account: &AccountName) -> Result<Option<String>, CronError> {
        let outcome = Self::run_bounded(
            self.distro.crontab_binary(),
            &[USER_FLAG, account.as_str(), LIST_FLAG],
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
    /// program's own exit status in the last case.
    pub(crate) fn install(&self, account: &AccountName, contents: &str) -> Result<(), CronError> {
        let path = Self::write_table(contents)?;

        let outcome = Self::run(
            self.distro.crontab_binary(),
            &[USER_FLAG, account.as_str(), &path.display().to_string()],
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

    /// Runs `program` with `arguments` as an argv array and waits for it.
    ///
    /// No shell, at any point (rules/security.md item 3): the arguments reach
    /// `execve` one by one, so there is no command line for anything to
    /// re-parse. `program` is an absolute path from the `DistroAdapter` and
    /// never a name resolved through `PATH`. Standard input is `/dev/null`
    /// rather than inherited, so a tool that decides to prompt fails instead of
    /// hanging a root daemon forever.
    ///
    /// Used for the INSTALL, whose output is a refusal message rather than an
    /// account's file. The listing uses [`Self::run_bounded`] instead.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] with a `code` of
    /// [`PROGRAM_UNAVAILABLE`] when the program cannot be started or waited
    /// for. A non-zero exit is not an error here — it is returned in the
    /// outcome, because each caller reads a status differently.
    fn run(program: &str, arguments: &[&str]) -> Result<CommandOutcome, CronError> {
        let output = Command::new(program)
            .args(arguments)
            .env(LOCALE_VARIABLE, LOCALE_VALUE)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .output()
            .map_err(|_| CronError::CrontabRefused {
                code: PROGRAM_UNAVAILABLE,
            })?;

        Ok(CommandOutcome {
            // -1 for a process killed by a signal: it did not exit, and
            // reporting 0 would read as success to every caller.
            status: output.status.code().unwrap_or(PROGRAM_UNAVAILABLE),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }

    /// Runs `program` with `arguments` and reads at most `ceiling` bytes of its
    /// standard output.
    ///
    /// [`Self::run`]'s `Command::output()` reads to end of output, which is
    /// correct for a program whose answer this agent chose and wrong for one
    /// whose answer is an account's own file. This is the same spawn with the
    /// read bounded: `ceiling + 1` bytes are taken, so exceeding the ceiling is
    /// DETECTED rather than silently truncated, and a program that has more to
    /// say is killed rather than left blocked on a pipe nobody is draining.
    ///
    /// Standard error is read afterwards and bounded too. It is small for every
    /// program this area runs, and it is read second because the answer that
    /// has to be bounded is the one on standard output.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] with a `code` of
    /// [`PROGRAM_UNAVAILABLE`] when the program cannot be started, its output
    /// cannot be taken or read, or it cannot be waited for; and with a `code` of
    /// [`TABLE_TOO_LARGE`] when it wrote more than `ceiling` bytes.
    fn run_bounded(
        program: &str,
        arguments: &[&str],
        ceiling: u64,
    ) -> Result<CommandOutcome, CronError> {
        let unavailable = || CronError::CrontabRefused {
            code: PROGRAM_UNAVAILABLE,
        };

        let mut child = Command::new(program)
            .args(arguments)
            .env(LOCALE_VARIABLE, LOCALE_VALUE)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|_| unavailable())?;

        let (Some(mut output), Some(errors)) = (child.stdout.take(), child.stderr.take()) else {
            let _ = child.kill();
            let _ = child.wait();

            return Err(unavailable());
        };

        // Standard error is consumed CONCURRENTLY with standard output, from
        // the moment the child exists, and this thread is the whole of that
        // property. A pipe nobody is reading fills at about 64 KiB, and a child
        // blocked writing into a full one never exits — so reading standard
        // output to its end FIRST and standard error afterwards deadlocks both,
        // in the root daemon, with no timeout anywhere to end it. Measured: with
        // the two reads sequential, the named test never returns.
        //
        // That is precisely the class `Command::output()` avoids by reading the
        // two concurrently, and `output()` is what this function replaced; the
        // property has to come back with it rather than rest on an argument
        // about how `crontab(1)` happens to behave.
        let collector = thread::spawn(move || drain_bounded(errors, ceiling));

        // One byte past the ceiling, so a table exactly at it is accepted and
        // one byte over is refused rather than quietly shortened.
        let mut stdout = Vec::new();
        let taken = output
            .by_ref()
            .take(ceiling.saturating_add(1))
            .read_to_end(&mut stdout);

        if taken.is_err() || stdout.len() as u64 > ceiling {
            let too_large = taken.is_ok();
            // Killed rather than drained: the point of a ceiling is not to read
            // the rest more politely. The kill closes the child's end of both
            // pipes, so the drain reaches end of input and the join below
            // returns rather than waiting on a process that is gone.
            let _ = child.kill();
            let _ = child.wait();
            let _ = collector.join();

            return Err(CronError::CrontabRefused {
                code: if too_large {
                    TABLE_TOO_LARGE
                } else {
                    PROGRAM_UNAVAILABLE
                },
            });
        }

        // Joined BEFORE the wait, so no path can return while the thread still
        // holds the pipe. The drain ends at end of input, which the child
        // reaches by exiting — and nothing this function does can stop it
        // exiting any more.
        //
        // `unwrap_or_default` and not `unwrap`: the drain has no panicking path,
        // and if it somehow acquired one, empty standard error means the
        // absent-crontab branch in `read_outcome` is simply not taken. That is a
        // refusal, which is the direction this whole area fails in.
        let stderr = collector.join().unwrap_or_default();
        let status = child.wait().map_err(|_| unavailable())?;

        Ok(CommandOutcome {
            status: status.code().unwrap_or(PROGRAM_UNAVAILABLE),
            stdout: String::from_utf8_lossy(&stdout).into_owned(),
            stderr: String::from_utf8_lossy(&stderr).into_owned(),
        })
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

/// The bounded spawn, reachable by a test.
///
/// [`CrontabSpool::run_bounded`] is an associated function of a type a test
/// cannot usefully build — it would need a real `DistroAdapter` and a real
/// account — while the two properties worth testing are properties of the SPAWN
/// itself: which environment the child gets, and whether a child that floods
/// standard error can still exit. This is the same split
/// `ops::sites`' `follow_as` uses, and it is `#[cfg(test)]` so nothing outside
/// the tests can reach the spawn by another route.
#[cfg(test)]
fn run_bounded_for_test(
    program: &str,
    arguments: &[&str],
    ceiling: u64,
) -> Result<CommandOutcome, CronError> {
    CrontabSpool::run_bounded(program, arguments, ceiling)
}

/// Reads at most `ceiling` bytes from `stream`, then reads the rest away.
///
/// Run on its own thread by [`CrontabSpool::run_bounded`], for the whole life of
/// the child. Two halves, and **the deadlock is removed by the thread, not by
/// either of them** — that is written down here because an earlier version of
/// this comment credited the second half with it and was measured to be wrong:
///
/// 1. **Keep a bounded prefix.** Standard error is a program's own message, but
///    it is a program handed an account's file, so what it echoes back is not
///    bounded by anything this agent chose. `Read::take` bounds what is kept.
/// 2. **Then read the remainder and throw it away.** What this buys is the
///    child's own EXIT STATUS. Stopping at the ceiling would return from this
///    function and drop `stream`, closing the read end — after which the child's
///    next write earns `SIGPIPE` and it dies by a signal, so `code()` is `None`
///    and the caller is told `-1` instead of what the program actually decided.
///    Measured: with the drain removed, a child that outruns the ceiling comes
///    back as `-1`; with it, as its own status.
///
///    It is NOT what stops the hang. Closing the read end unblocks the child
///    just as well, by killing it — which is why removing this line leaves
///    `a_child_that_floods_standard_error_does_not_deadlock_the_read` green and
///    reddens `a_child_that_outruns_the_ceiling_still_reports_its_own_status`
///    instead.
///
/// It returns bytes rather than a `Result`: a stream that cannot be read has
/// nothing to say, and the caller has no different action for the two cases.
fn drain_bounded(mut stream: ChildStderr, ceiling: u64) -> Vec<u8> {
    let mut kept = Vec::new();
    if stream
        .by_ref()
        .take(ceiling)
        .read_to_end(&mut kept)
        .is_err()
    {
        return kept;
    }

    let _ = io::copy(&mut stream, &mut io::sink());

    kept
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
