//! The [`RestrictedDumpHost`] that really runs the database client.

use std::fs::File;
use std::io::Write as _;
use std::path::PathBuf;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::grant_pattern_for::grant_pattern_for;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::bound_or::bound_or;
use crate::backup::model::import_user::ImportUser;
use crate::backup::restricted_dump_host::RestrictedDumpHost;
use crate::backup::run_discarding::run_discarding;

/// The client flag that makes a load read SQL and print nothing conversational.
const CLIENT_BATCH: &str = "--batch";

/// The client flag that runs one statement given on the command line.
const CLIENT_EXECUTE: &str = "--execute";

/// The client flag that prints rows with no header line.
const CLIENT_SKIP_COLUMN_NAMES: &str = "--skip-column-names";

/// The question that lists the temporary users, by their exact prefix.
///
/// `LEFT(...) =` and not `LIKE`: in a pattern `_` matches any character, so
/// `maran_imp_%` would also match a name this agent never made. The rows are
/// narrowed again, exactly, by [`ImportUser::from_listed`].
const LIST_IMPORT_USERS: &str =
    "SELECT user FROM mysql.user WHERE LEFT(user, 10) = 'maran_imp_' AND host = 'localhost'";

/// The client flag that turns the client's own commands off for input that is
/// piped to it.
///
/// A second layer behind the dump's pre-scan, not the first: the scan refuses a
/// client command by reading the text, and this stops the client acting on one
/// the scan failed to see. It is NOT verified here on every server version the
/// platforms ship; the pre-scan does not rely on it.
const CLIENT_BINARY_MODE: &str = "--binary-mode";

/// The client flag that refuses `LOAD DATA LOCAL INFILE`, which makes the CLIENT
/// read a file and send it to the server.
const CLIENT_NO_LOCAL_INFILE: &str = "--local-infile=0";

/// The client flag that pins the client's own character set to one in which no
/// multi-byte character holds an ASCII byte.
///
/// The pre-scan reads bytes. A client configured for a character set such as
/// `sjis` would skip a backslash or quote that the scan sees, and part from it on
/// where a literal ends.
const CLIENT_CHARACTER_SET: &str = "--default-character-set=utf8mb4";

/// The prefix of the option that names the credentials file. It must be the
/// first argument the client is given, or the client refuses it.
const DEFAULTS_EXTRA_FILE: &str = "--defaults-extra-file=";

/// Runs the real database client for the restricted loader.
///
/// The one place that spawns anything for it. The binary comes from the
/// `DistroAdapter` once, at construction, as every host's does.
///
/// The temporary user's password has two destinations and neither is a command
/// line. The statement that creates the user reaches the client on its standard
/// input. The load's client reads the credentials from an option file this host
/// writes into a directory only root can enter, with a name the file system
/// chose, and deletes when the load ends on any path.
pub struct ProcessRestrictedDumpHost {
    /// Absolute path of the database client.
    client_binary: AdapterBinary,
    /// A directory only root can read, where the credentials file is written.
    credentials_directory: PathBuf,
}

impl ProcessRestrictedDumpHost {
    /// Creates the host, taking the client from `distro`.
    ///
    /// `credentials_directory` MUST be root-only (the bulk scratch the backup
    /// area already keeps is the place). This type does not check it: the
    /// inode discipline for that tree is `root_only_chain`'s.
    #[must_use]
    pub fn new(distro: &dyn DistroAdapter, credentials_directory: PathBuf) -> Self {
        Self {
            client_binary: distro.mysql_client_binary(),
            credentials_directory,
        }
    }
}

/// The statements that create `user` and grant it one database, as the client
/// reads them on its standard input.
///
/// The database goes through [`grant_pattern_for`] and not in as the identifier
/// it is everywhere else: a database-level `GRANT` reads its name as a pattern
/// in which `_` and `%` are wildcards, so `acct_my_db` unescaped also names
/// `acctxmy_db`, another tenant's database, and the temporary user would hold
/// every privilege on it while a hostile dump loaded.
fn creation_statements(
    user: &ImportUser,
    password: &SecretString,
    database: &DatabaseName,
) -> String {
    format!(
        "CREATE USER '{name}'@'localhost' IDENTIFIED BY '{password}';\n\
         GRANT ALL PRIVILEGES ON `{pattern}`.* TO '{name}'@'localhost';\n",
        name = user.name(),
        password = password.expose(),
        pattern = grant_pattern_for(database.as_str()),
    )
}

impl RestrictedDumpHost for ProcessRestrictedDumpHost {
    /// Sends the creation and the grant to the client on its standard input.
    ///
    /// The name of the database is interpolated as a backtick-quoted identifier
    /// and is a [`DatabaseName`], whose alphabet holds nothing to close the
    /// quoting with; the user and password are hex digits. The grant's name is
    /// written by `creation_statements` as a pattern, escaped.
    ///
    /// # Errors
    ///
    /// [`BackupError::LoadFailed`] and [`BackupError::Bound`].
    fn create_import_user(
        &self,
        user: &ImportUser,
        password: &SecretString,
        database: &DatabaseName,
    ) -> Result<(), BackupError> {
        let statements = creation_statements(user, password, database);

        let outcome = run_discarding(
            self.client_binary.as_path(),
            &[CLIENT_BATCH],
            CallClass::Database,
            ChildInput::Bytes(statements.as_bytes()),
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::LoadFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::LoadFailed {
                status: outcome.status,
            });
        }

        Ok(())
    }

    /// Writes the credentials file, runs the client with the dump on its
    /// standard input, and lets the file go.
    ///
    /// The file is a temporary one: created by the file system with a name
    /// nobody chose, readable by its owner alone, and deleted when it goes out
    /// of scope, which is on every path out of this function.
    ///
    /// The run is bounded by the [`CallClass::Bulk`] deadline, after which the
    /// client and its process group are killed. The client's output is never
    /// read: a client refusing halfway prints the statement it choked on, and
    /// that is a customer's text.
    ///
    /// # Errors
    ///
    /// [`BackupError::LoadFailed`], including when the credentials file cannot
    /// be written, and [`BackupError::Bound`].
    fn load_dump_as(
        &self,
        user: &ImportUser,
        password: &SecretString,
        dump: File,
    ) -> Result<(), BackupError> {
        let unavailable = || BackupError::LoadFailed {
            status: PROGRAM_UNAVAILABLE,
        };

        let mut credentials = tempfile::Builder::new()
            .prefix("maran-import-")
            .suffix(".cnf")
            .tempfile_in(&self.credentials_directory)
            .map_err(|_| unavailable())?;
        write!(
            credentials,
            "[client]\nuser={}\npassword=\"{}\"\n",
            user.name(),
            password.expose()
        )
        .map_err(|_| unavailable())?;
        credentials.flush().map_err(|_| unavailable())?;
        let path = credentials.path().to_str().ok_or_else(unavailable)?;
        let defaults = format!("{DEFAULTS_EXTRA_FILE}{path}");

        let outcome = run_discarding(
            self.client_binary.as_path(),
            &[
                defaults.as_str(),
                CLIENT_BATCH,
                CLIENT_BINARY_MODE,
                CLIENT_CHARACTER_SET,
                CLIENT_NO_LOCAL_INFILE,
            ],
            CallClass::Bulk,
            ChildInput::File(dump),
        )
        .map_err(|error| bound_or(error, unavailable()))?;
        if outcome.status != 0 {
            return Err(BackupError::LoadFailed {
                status: outcome.status,
            });
        }

        Ok(())
    }

    /// Drops the user if it is there.
    ///
    /// # Errors
    ///
    /// [`BackupError::DropFailed`] and [`BackupError::Bound`].
    fn drop_import_user(&self, user: &ImportUser) -> Result<(), BackupError> {
        let statement = format!("DROP USER IF EXISTS '{}'@'localhost'", user.name());
        let arguments = [CLIENT_BATCH, CLIENT_EXECUTE, statement.as_str()];

        let outcome = run_discarding(
            self.client_binary.as_path(),
            &arguments,
            CallClass::Database,
            ChildInput::Null,
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::DropFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::DropFailed {
                status: outcome.status,
            });
        }

        Ok(())
    }

    /// Asks the server which users carry the temporary prefix.
    ///
    /// # Errors
    ///
    /// [`BackupError::ImportUsersUnlistable`] and [`BackupError::Bound`].
    fn leftover_import_users(&self) -> Result<Vec<ImportUser>, BackupError> {
        let arguments = [
            CLIENT_BATCH,
            CLIENT_SKIP_COLUMN_NAMES,
            CLIENT_EXECUTE,
            LIST_IMPORT_USERS,
        ];

        let outcome = spawn_argv(
            self.client_binary.as_path(),
            &arguments,
            CallClass::Database,
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::ImportUsersUnlistable {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::ImportUsersUnlistable {
                status: outcome.status,
            });
        }

        Ok(outcome
            .stdout
            .lines()
            .filter_map(|line| ImportUser::from_listed(line.trim()))
            .collect())
    }
}

#[cfg(test)]
#[path = "../tests/backup/process_restricted_dump_host_tests.rs"]
mod tests;
