//! The [`SftpHost`] that actually touches this machine.

use std::fs;
use std::io;
use std::io::Write as _;
use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::process::{Command, Stdio};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::utils::system_accounts::system_accounts;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError, write_config};
use crate::sftp::model::account_ownership::AccountOwnership;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// Runs the real `useradd`, `userdel` and `chpasswd`, and installs the real
/// mount unit.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing lives in the
/// operations, where it is tested against a fake. What is left here is
/// spawning, writing a directory, and handing a unit file to the config-write
/// protocol.
pub struct ProcessSftpHost;

impl ProcessSftpHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessSftpHost {
    /// The host has no state, so the default is the only value there is.
    fn default() -> Self {
        Self::new()
    }
}

impl ConfigHost for ProcessSftpHost {
    /// Spawns `program` with `arguments` as an argv array, for the
    /// config-write protocol's validator and reload.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse. `program` comes from the `DistroAdapter`'s
    /// allow-list and never from a request.
    ///
    /// The spawn itself is [`spawn_argv`], shared with every other host that
    /// runs an argv array — but only THIS impl's spawn. The password change
    /// below keeps its own `Command`, because it pipes a secret to the child's
    /// standard input, which is exactly what the shared body does not do.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be
    /// started, carrying the operating system's reason. A program that started
    /// and exited non-zero is not an error: its status comes back in the
    /// outcome for the protocol above to judge.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        spawn_argv(program, arguments).map_err(|error| SafeWriteError::SpawnFailed {
            program: program.to_owned(),
            reason: error.to_string(),
        })
    }
}

impl SftpHost for ProcessSftpHost {
    /// Spawns `program` with `arguments` as an argv array, writing `stdin` to
    /// its standard input when there is one.
    ///
    /// No shell, at any point. When there is nothing to write, standard input
    /// is `/dev/null` rather than inherited: a tool that decides to prompt then
    /// fails instead of hanging a root daemon forever. When there is, the pipe
    /// is dropped as soon as the line has been written — `chpasswd` reads to end
    /// of input, and a pipe left open is the same hang by a different route.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::SpawnFailed`] with a `code` of `-1` when the
    /// program cannot be started, its standard input cannot be taken or
    /// written, or it cannot be waited for.
    fn run(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: Option<&str>,
    ) -> Result<CommandOutcome, SftpError> {
        let mut child = Command::new(program)
            .args(arguments)
            .stdin(if stdin.is_some() {
                Stdio::piped()
            } else {
                Stdio::null()
            })
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|_| SftpError::program_unavailable())?;

        if let Some(line) = stdin {
            let Some(mut pipe) = child.stdin.take() else {
                return Err(SftpError::program_unavailable());
            };

            let written = pipe.write_all(line.as_bytes());
            // Closed here, before the wait: the tool reads to end of input, so
            // waiting on a process whose input pipe is still open waits forever.
            drop(pipe);

            if written.is_err() {
                let _ = child.kill();
                let _ = child.wait();

                return Err(SftpError::program_unavailable());
            }
        }

        let output = child
            .wait_with_output()
            .map_err(|_| SftpError::program_unavailable())?;

        Ok(CommandOutcome {
            status: output.status.code().unwrap_or(-1),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }

    /// Resolves `account` through the host's password database.
    ///
    /// `AccountIds::resolve` is the one lookup in this repository, reused
    /// rather than a second `getpwnam` written here: it already refuses `root`,
    /// the root group and the system id range, so an SFTP login cannot be
    /// created with a privileged identity even if a name that resolves to one
    /// somehow reached this point.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::AccountMissing`] for every failure of the lookup.
    /// The lookup's own error is deliberately not carried across: this area's
    /// error type has no field that could hold one, and the panel's question is
    /// answered by the variant rather than by the detail.
    fn account_ownership(&self, account: &AccountName) -> Result<AccountOwnership, SftpError> {
        let ids = AccountIds::resolve(account).map_err(|_| SftpError::AccountMissing)?;

        Ok(AccountOwnership {
            uid: ids.uid(),
            gid: ids.gid(),
        })
    }

    /// Creates `path` with `create_dir_all`, then sets `mode` on it.
    ///
    /// The mode is set after the fact rather than left to the creation, because
    /// creation applies the process umask and the agent does not control what
    /// its unit file was started with. A jail that came out `0775` because of an
    /// inherited umask is a jail OpenSSH refuses to chroot into, and the failure
    /// would appear as a login that disconnects rather than as anything visible
    /// here.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] when the directory cannot be created or
    /// its mode cannot be set.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), SftpError> {
        fs::create_dir_all(path).map_err(|_| SftpError::JailFailed)?;
        fs::set_permissions(path, fs::Permissions::from_mode(mode))
            .map_err(|_| SftpError::JailFailed)?;

        Ok(())
    }

    /// Delegates to the one config-write protocol and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] for every failure of the protocol. The
    /// protocol's own error is deliberately not carried across: its variants
    /// hold a tool's standard error, and this area's error type has no field
    /// that could hold one.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SftpError> {
        write_config(self, target, contents, validator, reload).map_err(|_| SftpError::JailFailed)
    }

    /// Lists `account`'s logins by reading the host's own passwd file.
    ///
    /// `passwd_database` comes from the `DistroAdapter`: where the local
    /// password database lives is a fact of the platform, and `ops` names no
    /// absolute system path of its own (rules/architecture.md).
    ///
    /// The file's TEXT is turned into rows by
    /// [`maran_agent_core::utils::system_accounts::system_accounts`], which is
    /// also what the monitoring area enumerates accounts with: where a home
    /// field sits in a passwd line is a question about the host and not about
    /// SFTP, so the two areas read it through one unit rather than each
    /// counting fields for itself. What stays here is the only part that IS
    /// about SFTP — which of those rows is a login of this account.
    ///
    /// The file rather than a `getpwent` walk, and that is a deliberate
    /// narrowing: enumerating the password database through libc means holding
    /// iterator state across a root process's threads, whereas every login this
    /// panel creates is a local entry in this one file. What is given up is
    /// visibility of logins served by LDAP or another name service — which this
    /// panel never creates, and which it must not delete.
    ///
    /// A candidate must ALSO have its passwd home set to `jail_directory`,
    /// which is what tells one account's login from another ACCOUNT of the same
    /// spelling: `alice_bob` is both the account `alice_bob` and the login `bob`
    /// of account `alice`, and only the home field distinguishes them.
    ///
    /// Each name is then put through [`SftpUserName::decode`], which is the
    /// inverse of the constructor that built it and lives beside it, so nothing
    /// outside this agent's own naming convention is ever reported and one
    /// account's name can never alias another's: `alice_bob_deploy` decodes to
    /// `alice_bob` and is not `alice`'s.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::AccountMissing`] when the passwd file cannot be
    /// read. An account with no logins is an empty list.
    fn account_logins(
        &self,
        passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<SftpUserName>, SftpError> {
        let passwd = fs::read_to_string(passwd_database).map_err(|_| SftpError::AccountMissing)?;

        let mut logins: Vec<SftpUserName> = system_accounts(&passwd)
            .into_iter()
            .filter(|row| row.home == jail_directory)
            .filter_map(|row| SftpUserName::decode(account, &row.name))
            .collect();
        // Sorted so that two calls against an unchanged host remove the logins
        // in the same order, whatever order the file happened to hold them in.
        logins.sort_by(|left, right| left.as_str().cmp(right.as_str()));
        logins.dedup();

        Ok(logins)
    }

    /// Answers `Path::exists`, which is `false` for a path that cannot be
    /// inspected as well as for one that is not there.
    fn path_exists(&self, path: &Path) -> bool {
        path.exists()
    }

    /// Removes the file, treating "it was not there" as success.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] for every other failure.
    fn remove_file(&self, path: &Path) -> Result<(), SftpError> {
        match fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
            Err(_) => Err(SftpError::JailFailed),
        }
    }

    /// Removes the directory with `remove_dir`, which refuses a directory that
    /// is not empty — the property the jail teardown depends on, because a
    /// mount point that still holds the account's home must NOT be walked into.
    ///
    /// "It was not there" is success.
    ///
    /// # Errors
    ///
    /// Returns [`SftpError::JailFailed`] for every other failure, including a
    /// directory that is not empty and a mount point that is still mounted.
    fn remove_directory(&self, path: &Path) -> Result<(), SftpError> {
        match fs::remove_dir(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
            Err(_) => Err(SftpError::JailFailed),
        }
    }
}
