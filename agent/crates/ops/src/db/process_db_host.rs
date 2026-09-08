//! The [`DbHost`] that actually runs the database client on this machine.

use std::io::{Read as _, Write as _};
use std::process::{Command, Stdio};

use maran_agent_core::utils::apply_child_environment::apply_child_environment;
use maran_distro::DistroAdapter;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;

/// The most standard output this host will take from one statement.
///
/// A ceiling exists because the widest statement this area sends —
/// `SHOW DATABASES` — returns a row per database on the server, a number the
/// agent does not control. Reading "however much there is" into a `String` is
/// the shape that turns a host with a runaway number of databases into a root
/// daemon out of memory, so the read is bounded and output past the ceiling is
/// refused rather than truncated: a truncated listing is a listing that silently
/// omits a customer's database.
///
/// One mebibyte is roughly sixteen thousand names at the sixty-four byte
/// identifier limit, and every other statement here answers with a single line.
const MAXIMUM_OUTPUT_BYTES: u64 = 1024 * 1024;

/// The most bytes of statement this host will hand the client.
///
/// The statement is written into the client's standard input and the pipe is
/// closed before anything is read back, which is only safe while the statement
/// cannot fill the pipe: a writer blocked on a full pipe and a reader that has
/// not started yet is a deadlock in a root daemon. Linux gives a pipe 64 KiB,
/// and the widest statement this area builds is a `CREATE USER` of a 64-byte
/// identifier and a 128-byte password — three orders of magnitude below this
/// ceiling. So the bound is not a limit anybody is expected to meet; it is what
/// turns "the write cannot block" from an argument into a checked property.
const MAXIMUM_STATEMENT_BYTES: usize = 4096;

/// Runs the real client against the real server — the one place in this area
/// that spawns a process.
///
/// Deliberately the smallest piece of the area: every decision worth reviewing
/// lives in the operations, where it is tested against a fake. What is left here
/// is spawning, a bounded read, and handing a failure to
/// `DbError::from_client`.
pub struct ProcessDbHost {
    /// Absolute path of the client, taken from the distro adapter once at
    /// construction. It is a platform fact, so it comes from the adapter and
    /// never from a literal in this crate (rules/architecture.md), and it is
    /// stored rather than re-asked so that the argv array cannot be built from
    /// anything a request influenced.
    client_binary: String,
}

impl ProcessDbHost {
    /// Creates the host, taking the client's path from `distro`.
    #[must_use]
    pub fn new(distro: &dyn DistroAdapter) -> Self {
        Self {
            client_binary: distro.mysql_client_binary().to_owned(),
        }
    }
}

impl DbHost for ProcessDbHost {
    /// Spawns the client with an argv array, writes the statement to its
    /// standard input, and returns its standard output.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one and there is no command line for
    /// anything to re-parse. `--batch` and `--skip-column-names` ask for the
    /// unformatted, header-free output the callers read.
    ///
    /// # Why the statement travels on standard input and not in the argv array
    ///
    /// A `CREATE USER … IDENTIFIED BY '<value>'` and an `ALTER USER … IDENTIFIED
    /// BY '<value>'` carry a customer's database password in cleartext, and an
    /// argv array is not private: `/proc/<pid>/cmdline` is world-readable on
    /// every supported system — measured, `-r--r--r-- root root` on a root
    /// process — so while the client ran, ANY local uid could read the password
    /// out of it. That is every other tenant's php-fpm pool on a shared host.
    /// The client scrubs `-p<value>` from its own argv and does not scrub
    /// `--execute`, so the flag could not be relied on to hide it either.
    /// Standard input is a pipe between two processes and appears in no
    /// listing.
    ///
    /// The credential the CONNECTION uses is a separate question, and there is
    /// none: the agent reaches the server over the local socket, authenticated
    /// by its uid.
    ///
    /// # One statement, still
    ///
    /// `--execute` used to be what made "exactly one statement" true, and
    /// standard input does not: the client reads a script from it. So the
    /// property is asserted here instead of inherited from a flag — a statement
    /// carrying `;` or any control character (a newline is how a second
    /// statement would arrive) is refused before the client is started. No
    /// caller can build one today, because every value interpolated into these
    /// statements is a validated type whose alphabet excludes both; this is the
    /// guard for the caller that is written next.
    ///
    /// The write happens before anything is read back, which is safe only
    /// because the statement-size ceiling this file declares keeps the
    /// statement far below a pipe's capacity — see `MAXIMUM_STATEMENT_BYTES`.
    ///
    /// # Errors
    ///
    /// - [`DbError::Unparsable`] when the output is not UTF-8, or exceeds
    ///   the ceiling above.
    /// - `DbError::client_unavailable` when the statement is refused by the
    ///   guard above, when the client could not be started, or when its input
    ///   could not be delivered.
    /// - Whatever `DbError::from_client` makes of a non-zero exit.
    fn execute(&self, statement: &str) -> Result<String, DbError> {
        run_client(&self.client_binary, statement)
    }
}

/// The body of [`ProcessDbHost::execute`], with the client's path a parameter.
///
/// Separated for one reason: the property this function exists to hold — that
/// the statement, and therefore a customer's password, never becomes an argv
/// element — can only be observed by running a real program and looking at what
/// it received. A test can point this at a stand-in that records its own argv
/// and its own standard input; it cannot point [`ProcessDbHost`] anywhere,
/// because that path comes from the `DistroAdapter`, which is exactly the
/// property `ProcessDbHost` is there to keep.
///
/// # Errors
///
/// As documented on [`ProcessDbHost::execute`].
fn run_client(client_binary: &str, statement: &str) -> Result<String, DbError> {
    if !is_single_statement(statement) {
        return Err(DbError::client_unavailable());
    }

    let mut command = Command::new(client_binary);
    apply_child_environment(&mut command);
    let mut child = command
        .args(["--batch", "--skip-column-names"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|_| DbError::client_unavailable())?;

    let Some(mut stdin) = child.stdin.take() else {
        let _ = child.kill();
        let _ = child.wait();

        return Err(DbError::client_unavailable());
    };

    // A trailing newline, and the handle dropped straight after: the client
    // executes what it has at end of input, and a pipe left open is a client
    // waiting for a statement that has already been written.
    let written = stdin
        .write_all(statement.as_bytes())
        .and_then(|()| stdin.write_all(b"\n"));
    drop(stdin);

    if written.is_err() {
        let _ = child.kill();
        let _ = child.wait();

        return Err(DbError::client_unavailable());
    }

    let Some(stdout) = child.stdout.take() else {
        let _ = child.kill();
        let _ = child.wait();

        return Err(DbError::client_unavailable());
    };

    // One byte past the ceiling, so that hitting it is distinguishable from
    // an answer that happens to be exactly the ceiling long.
    let mut captured = String::new();
    let read = stdout
        .take(MAXIMUM_OUTPUT_BYTES + 1)
        .read_to_string(&mut captured);
    if read.is_err() || captured.len() as u64 > MAXIMUM_OUTPUT_BYTES {
        // The client is still writing into a pipe nobody is draining, so it
        // would block until this process exits. Killing it is what makes the
        // ceiling a ceiling rather than a leak of one stuck client per call.
        let _ = child.kill();
        let _ = child.wait();

        return Err(DbError::Unparsable);
    }

    // Standard output is already at end of file and its handle is gone, so
    // this collects the exit status and standard error only. The client
    // writes one `ERROR …` line per refused statement, and exactly one
    // statement was sent.
    let finished = child
        .wait_with_output()
        .map_err(|_| DbError::client_unavailable())?;

    if !finished.status.success() {
        return Err(DbError::from_client(
            // -1 for a process killed by a signal: it did not exit, and
            // reporting 0 would read as success to every caller.
            finished.status.code().unwrap_or(-1),
            &String::from_utf8_lossy(&finished.stderr),
        ));
    }

    Ok(captured)
}

/// Whether `statement` is one statement this host is willing to send.
///
/// Two questions, both about what standard input would do with the text that
/// `--execute` used to answer by construction:
///
/// - a `;` and a control character (the newline among them) are the two ways a
///   second statement arrives in a script the client reads to end of input;
/// - [`MAXIMUM_STATEMENT_BYTES`] is what keeps the write to the client's pipe
///   from blocking before this process starts reading the answer.
///
/// It is a separate function so that both can be tested without a server.
fn is_single_statement(statement: &str) -> bool {
    statement.len() <= MAXIMUM_STATEMENT_BYTES
        && !statement.contains(';')
        && !statement.chars().any(char::is_control)
}

#[cfg(test)]
#[path = "../tests/db/process_db_host_tests.rs"]
mod tests;
