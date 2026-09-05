//! The [`DbHost`] that actually runs the database client on this machine.

use std::io::Read as _;
use std::process::{Command, Stdio};

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
    /// Spawns the client with an argv array and returns its standard output.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so the statement is one argument
    /// containing whatever it contains, and there is no command line for
    /// anything to re-parse. `--batch` and `--skip-column-names` ask for the
    /// unformatted, header-free output the callers read, and standard input is
    /// `/dev/null` so a client that decides to prompt fails instead of hanging
    /// a root daemon forever.
    ///
    /// No credentials appear in the argv array, because there are none: the
    /// connection is the local socket, authenticated by the agent's uid.
    ///
    /// # Errors
    ///
    /// - [`DbError::Unparsable`] when the output is not UTF-8, or exceeds
    ///   the ceiling above.
    /// - Whatever `DbError::from_client` makes of a non-zero exit, and
    ///   `DbError::client_unavailable` when the client could not be started.
    fn execute(&self, statement: &str) -> Result<String, DbError> {
        let mut child = Command::new(&self.client_binary)
            .args(["--batch", "--skip-column-names", "--execute", statement])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|_| DbError::client_unavailable())?;

        let Some(stdout) = child.stdout.take() else {
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
}
