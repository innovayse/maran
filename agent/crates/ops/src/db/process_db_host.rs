//! The [`DbHost`] that actually runs the database client on this machine.

use std::path::Path;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::utils::spawn_request::SpawnRequest;
use maran_distro::{AdapterBinary, DistroAdapter};

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

/// The most standard error this host will take from one statement.
///
/// The client writes one `ERROR …` line per refused statement and exactly one
/// statement is sent, so this is a ceiling nobody is expected to meet. Reaching
/// it is [`DbError::Bound`], not a classification: the text is never read for a
/// reason except the server's error number.
const MAXIMUM_ERROR_BYTES: u64 = 64 * 1024;

/// The client option that makes the SERVER stop waiting for a metadata lock
/// before the agent stops waiting for the client.
///
/// A statement such as `DROP DATABASE` or `DROP USER` waits for a metadata lock
/// held by anyone with an open transaction on the object, and the server's own
/// default for that wait is a day. The agent kills the client at the
/// [`CallClass::Database`] deadline (300 s), but a statement the server has
/// already accepted may be left waiting behind a customer's transaction and take
/// effect later, long after the agent reported it timed out. Giving the session
/// a `lock_wait_timeout` of 240 s, 60 s below that deadline, makes the server
/// abandon the wait first and answer with an error number, so the usual outcome
/// is a refusal and not a statement that is still pending.
///
/// It travels in the argv, not as a second statement: [`is_single_statement`]
/// refuses anything but one statement, and the option is a constant, so it
/// holds no customer value (the password-bearing statement still travels on
/// standard input, `/proc/<pid>/cmdline` shows none of it). `--init-command` is
/// an option of the MariaDB client, which both families' `mysql` is. It stays
/// an unmeasured assumption until the polygon suites run it on both families:
/// a client that rejected it would refuse every statement.
const LOCK_WAIT_OPTION: &str = "--init-command=SET SESSION lock_wait_timeout=240";

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
    /// anything a request influenced. An [`AdapterBinary`], which only the
    /// adapter can make, so no other path can be stored here at all.
    client_binary: AdapterBinary,
}

impl ProcessDbHost {
    /// Creates the host, taking the client's path from `distro`.
    #[must_use]
    pub fn new(distro: &dyn DistroAdapter) -> Self {
        Self {
            client_binary: distro.mysql_client_binary(),
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
    /// # Bounded
    ///
    /// The client runs under the [`CallClass::Database`] deadline, in a process
    /// group of its own that is killed whole when the deadline passes, with its
    /// standard output capped at `MAXIMUM_OUTPUT_BYTES` and its standard error
    /// at `MAXIMUM_ERROR_BYTES`. The two streams are drained concurrently, so a
    /// client that fills its error pipe before it closes its output can no
    /// longer wedge the call. The session also carries a server-side
    /// `lock_wait_timeout` (see `LOCK_WAIT_OPTION`) that fires before the
    /// deadline.
    ///
    /// No `--` is put in the argv: it holds no operand, only two constant
    /// options, and the statement is on standard input.
    ///
    /// # Errors
    ///
    /// - [`DbError::Unparsable`] when the output exceeds the ceiling above.
    ///   Output that is not valid UTF-8 is read lossily, as the core returns
    ///   it, rather than refused: every answer this area parses is an
    ///   identifier or a number.
    /// - [`DbError::Bound`] when the client was still running at the deadline
    ///   (the statement MAY nevertheless take effect on the server), or its
    ///   standard error went past its cap.
    /// - [`DbError::StatementRefused`] when the guard above refuses the
    ///   statement, in which case nothing is spawned at all.
    /// - [`DbError::ClientUnavailable`] when the client could not be started,
    ///   or its input could not be delivered.
    /// - [`DbError::ClientKilled`] when a signal the agent did not send ended the
    ///   client before it answered. The core reports such a client as having no
    ///   exit status and not WHICH signal, so the number is the unknown one.
    /// - Whatever `DbError::from_client` makes of a non-zero exit.
    ///
    /// The first three were one value — `ClientFailed { code: -1 }` — until the
    /// flake in this area's own tests was attributed to the wrong one of them
    /// twice. They are told apart by the VARIANT and never by a message: no
    /// variant of [`DbError`] can hold a string, because the string available
    /// here is the client's own output and the client quotes back what it
    /// refused, which on two paths is a customer's password
    /// (rules/security.md item 8).
    fn execute(&self, statement: &str) -> Result<String, DbError> {
        run_client(self.client_binary.as_path(), statement)
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
/// property `ProcessDbHost` is there to keep. The parameter is a `&Path` and not
/// an [`AdapterBinary`] because the latter can only be made inside the distro
/// crate; the only caller that holds a real path is [`ProcessDbHost`], whose
/// field is one.
///
/// The statement is copied once, with its trailing newline, into a buffer that
/// is overwritten before it is dropped: the core copies it again into the thread
/// that writes the pipe and overwrites that copy too. Best effort in both
/// places, and the same discipline as the pipe writer's.
///
/// # Errors
///
/// As documented on [`ProcessDbHost::execute`].
fn run_client(client_binary: &Path, statement: &str) -> Result<String, DbError> {
    if !is_single_statement(statement) {
        return Err(DbError::StatementRefused);
    }

    // A trailing newline: the client executes what it has at end of input, and
    // the core closes the pipe after the last byte.
    let mut script = Vec::with_capacity(statement.len() + 1);
    script.extend_from_slice(statement.as_bytes());
    script.push(b'\n');

    let answered = run_bounded(SpawnRequest {
        program: client_binary,
        arguments: &["--batch", "--skip-column-names", LOCK_WAIT_OPTION],
        class: CallClass::Database,
        input: ChildInput::Bytes(&script),
        output: ChildOutput::Capture {
            stdout_cap: MAXIMUM_OUTPUT_BYTES,
            stderr_cap: MAXIMUM_ERROR_BYTES,
        },
    });
    script.fill(0);
    // So that the overwrite is not removed as a store to memory about to be freed.
    std::hint::black_box(&script);

    let outcome = answered.map_err(|error| match error {
        SpawnError::Bound(bound) => bound_error(bound),
        _ => DbError::ClientUnavailable,
    })?;

    if outcome.status != 0 {
        // A status the core reports as -1 is a signal it did not send, and the
        // two are different answers to the operator's question: an exit code
        // means the client ran and the server replied, a signal means something
        // on this machine killed it mid-statement and nobody knows whether the
        // statement took effect. They used to be folded onto the same number.
        // No exit status is negative, so -1 is not ambiguous.
        if outcome.status < 0 {
            return Err(DbError::client_killed(None));
        }

        return Err(DbError::from_client(outcome.status, &outcome.stderr));
    }

    Ok(outcome.stdout)
}

/// Turns a bound into this area's error.
///
/// Standard output past its cap is the area's existing refusal, [`DbError::Unparsable`]:
/// a listing cut short omits a customer's database, so it is refused and never
/// truncated. Every other bound — the deadline, and standard error past its cap
/// — is [`DbError::Bound`], and is never `Unparsable` or `ClientFailed`: a
/// client that was killed has not answered, and nothing it printed was read.
fn bound_error(bound: BoundExceeded) -> DbError {
    match bound {
        BoundExceeded::OutputTooLarge {
            stream: OutputStream::Stdout,
            ..
        } => DbError::Unparsable,
        other => DbError::Bound(other),
    }
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
