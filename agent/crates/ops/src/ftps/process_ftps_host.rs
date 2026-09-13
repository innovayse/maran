//! The [`FtpsHost`] that actually touches this machine.

use std::io::{Read as _, Write as _};
use std::net::{Ipv4Addr, Ipv6Addr, SocketAddr, TcpListener, TcpStream};
use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::utils::apply_child_environment::apply_child_environment;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::utils::system_accounts::system_accounts;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::ftps::ftps_error::{FtpsError, PROGRAM_UNAVAILABLE};
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::candidate_outcome::CandidateOutcome;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError, write_config};
use crate::sftp::AccountOwnership;
use crate::ssl::{CertificateState, ProcessSslHost, certificate_state};

/// How often the candidate daemon is asked whether it has exited.
///
/// Short enough that a refusal — which is immediate on both families — is
/// noticed almost at once, long enough that the wait is not a spin.
const CANDIDATE_POLL: Duration = Duration::from_millis(25);

/// How long a connection to the control port, and a read of its greeting, may
/// take.
///
/// Bounded because this runs inside an rpc: a daemon that accepts a connection
/// and then says nothing must not hold the caller open, and the honest answer
/// after the timeout is the same as the answer to a refused connection —
/// nothing is serving.
const GREETING_TIMEOUT: Duration = Duration::from_secs(2);

/// How many bytes of the greeting are read.
///
/// A `220` line is a few dozen characters; the cap exists because the peer is a
/// socket and no read from one is allowed to be unbounded.
const GREETING_LIMIT: usize = 256;

/// Runs the real vsftpd and the real service manager, and opens the real
/// sockets.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing — which errno is
/// evidence about address families, what a greeting must begin with, whether a
/// candidate that outlived its deadline is good — lives in the operations, where
/// it is tested against a fake. What is left here is spawning, reading a file,
/// opening a socket, and handing content to the config-write protocol.
pub struct ProcessFtpsHost {
    /// The SSL area's real host, which the certificate question is delegated to.
    ///
    /// Composition rather than a second implementation: FTPS asks what material
    /// exists and never writes any, so it reads the store through the code that
    /// owns it instead of learning the store's layout for itself.
    ssl: ProcessSslHost,
}

impl ProcessFtpsHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            ssl: ProcessSslHost::new(),
        }
    }
}

impl Default for ProcessFtpsHost {
    /// The host holds only its delegate, so the default is the only value there
    /// is.
    fn default() -> Self {
        Self::new()
    }
}

impl ConfigHost for ProcessFtpsHost {
    /// Spawns `program` with `arguments` as an argv array, for the config-write
    /// protocol's validator and reload steps.
    ///
    /// No shell is involved at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse. `program` comes from the `DistroAdapter`'s
    /// allow-list and never from a request.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be started
    /// at all. A program that ran and exited non-zero is not an error here: its
    /// status comes back in the outcome for the protocol to judge.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        spawn_argv(program, arguments).map_err(|error| SafeWriteError::SpawnFailed {
            program: program.to_owned(),
            reason: error.to_string(),
        })
    }
}

impl FtpsHost for ProcessFtpsHost {
    /// Spawns `program` with `arguments` as an argv array and waits for it.
    ///
    /// The shared spawn body, like every other host that runs a plain argv array:
    /// nothing here pipes a secret or reads under a ceiling, so there is nothing
    /// this area's spawn would lose by being the common one.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when the program
    /// cannot be started at all.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FtpsError> {
        spawn_argv(program, arguments).map_err(|_| FtpsError::program_unavailable())
    }

    /// Reads `target`, answering `None` when it is not there.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ConfigUnreadable`] when the file exists and cannot be
    /// read.
    fn read_config(&self, target: &Path) -> Result<Option<String>, FtpsError> {
        match std::fs::read_to_string(target) {
            Ok(contents) => Ok(Some(contents)),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(FtpsError::ConfigUnreadable),
        }
    }

    /// Hands `contents` to the one implementation of the config-write protocol
    /// and translates its typed failure into this area's.
    ///
    /// Adds nothing of its own — no second temporary file, no second rollback.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ServiceRefused`] when the protocol's validator or
    /// reload step exited non-zero, since for this file both of those steps are
    /// the service manager acting on the daemon. Returns
    /// [`FtpsError::SpawnFailed`] when one of them could not be started, and
    /// [`FtpsError::ConfigWrite`] for the protocol's mechanical failures,
    /// carrying its own message for the operator log.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), FtpsError> {
        write_config(self, target, contents, validator, reload).map_err(|error| match error {
            SafeWriteError::ValidationFailed { .. } | SafeWriteError::ReloadFailed { .. } => {
                FtpsError::ServiceRefused {
                    unit: AgentPaths::FTPS_UNIT.to_owned(),
                }
            }
            SafeWriteError::SpawnFailed { .. } => FtpsError::program_unavailable(),
            other => FtpsError::ConfigWrite {
                reason: other.to_string(),
            },
        })
    }

    /// Stages `contents` in a root-only temporary file and runs `program` against
    /// it for at most `deadline`, killing it if it survives.
    ///
    /// The candidate is written `0600` under the agent's own scratch directory,
    /// so a configuration naming a private key's path is not readable by anyone
    /// else even for the moment it exists. The file's path is this method's own
    /// and never a caller's, so no request value reaches an argument vector.
    ///
    /// Output is read AFTER the child has finished or been killed, from pipes
    /// that were sized by the kernel: a refusal prints one short line at most, and
    /// an accepted candidate prints nothing before it is killed.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] when the candidate cannot be staged or
    /// the program cannot be started.
    fn run_candidate(
        &self,
        program: &str,
        contents: &str,
        arguments: &[&str],
        deadline: Duration,
    ) -> Result<CandidateOutcome, FtpsError> {
        let staged = stage_candidate(contents)?;
        let Some(candidate_path) = staged.path().to_str() else {
            return Err(FtpsError::program_unavailable());
        };

        let mut command = Command::new(program);
        apply_child_environment(&mut command);
        let mut child = command
            .arg(candidate_path)
            .args(arguments)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|_| FtpsError::program_unavailable())?;

        let started = Instant::now();
        loop {
            match child.try_wait() {
                Ok(Some(_)) => {
                    let output = child
                        .wait_with_output()
                        .map_err(|_| FtpsError::program_unavailable())?;
                    let mut printed = String::from_utf8_lossy(&output.stdout).into_owned();
                    printed.push_str(&String::from_utf8_lossy(&output.stderr));
                    return Ok(CandidateOutcome::Exited { output: printed });
                }
                Ok(None) => {
                    if started.elapsed() >= deadline {
                        // Still serving when the deadline expired: the daemon read
                        // the file, bound its socket and had no objection.
                        let _ = child.kill();
                        let _ = child.wait();
                        return Ok(CandidateOutcome::StillRunning);
                    }
                    std::thread::sleep(CANDIDATE_POLL);
                }
                Err(_) => return Err(FtpsError::program_unavailable()),
            }
        }
    }

    /// Binds `127.0.0.1:0`, reads the port the kernel assigned and drops the
    /// socket.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when even a
    /// loopback bind fails.
    fn ephemeral_port(&self) -> Result<u16, FtpsError> {
        let listener = TcpListener::bind(SocketAddr::from((Ipv4Addr::LOCALHOST, 0)))
            .map_err(|_| FtpsError::program_unavailable())?;
        let port = listener
            .local_addr()
            .map_err(|_| FtpsError::program_unavailable())?
            .port();
        drop(listener);
        Ok(port)
    }

    /// Binds an IPv6 listening socket at `[::]:0` the way the daemon would, and
    /// drops it.
    ///
    /// A listening bind and not a connection, because that is the operation
    /// vsftpd performs and the one a kernel without IPv6 refuses.
    ///
    /// # Errors
    ///
    /// Returns whatever the operating system said, unchanged — the caller
    /// classifies it.
    fn bind_ipv6_listener(&self) -> std::io::Result<()> {
        TcpListener::bind(SocketAddr::from((Ipv6Addr::UNSPECIFIED, 0))).map(drop)
    }

    /// Connects to `port` on the loopback address and reads whatever the peer
    /// says first.
    ///
    /// Every failure is the same answer — nothing is serving — so this returns
    /// `None` rather than an error: a refused connection, a timeout and an empty
    /// read are one fact for the caller, and giving them three shapes would push
    /// a decision into the caller that it cannot act on differently.
    fn control_port_greeting(&self, port: u16) -> Option<String> {
        let address = SocketAddr::from((Ipv4Addr::LOCALHOST, port));
        let mut stream = TcpStream::connect_timeout(&address, GREETING_TIMEOUT).ok()?;
        stream.set_read_timeout(Some(GREETING_TIMEOUT)).ok()?;

        let mut buffer = [0_u8; GREETING_LIMIT];
        let read = stream.read(&mut buffer).ok()?;
        if read == 0 {
            return None;
        }

        Some(String::from_utf8_lossy(&buffer[..read]).into_owned())
    }

    /// Answers what certificate material is installed for `domain`.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ConfigUnreadable`] when a file of the material exists
    /// and cannot be read.
    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, FtpsError> {
        certificate_state(&self.ssl, domain).map_err(|_| FtpsError::ConfigUnreadable)
    }

    /// Spawns `program` with `arguments` as an argv array and writes `stdin` to
    /// its standard input.
    ///
    /// Its own `Command` rather than the shared [`spawn_argv`] body, because
    /// this is the one spawn in the area that pipes a secret: the shared body
    /// gives the child `/dev/null` and has nothing to write. The pipe is
    /// dropped as soon as the line has been written — `chpasswd` reads to end
    /// of input, and a pipe left open hangs a root daemon's task forever.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when the program
    /// cannot be started, its standard input cannot be taken or written, or it
    /// cannot be waited for.
    fn run_with_stdin(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: &str,
    ) -> Result<CommandOutcome, FtpsError> {
        let mut command = Command::new(program);
        apply_child_environment(&mut command);
        let mut child = command
            .args(arguments)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|_| FtpsError::program_unavailable())?;

        let Some(mut pipe) = child.stdin.take() else {
            return Err(FtpsError::program_unavailable());
        };
        let written = pipe.write_all(stdin.as_bytes());
        // Closed here, before the wait: the tool reads to end of input, so
        // waiting on a process whose input pipe is still open waits forever.
        drop(pipe);

        if written.is_err() {
            let _ = child.kill();
            let _ = child.wait();

            return Err(FtpsError::program_unavailable());
        }

        let output = child
            .wait_with_output()
            .map_err(|_| FtpsError::program_unavailable())?;

        Ok(CommandOutcome {
            status: output.status.code().unwrap_or(PROGRAM_UNAVAILABLE),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }

    /// Resolves `account` through the host's password database.
    ///
    /// `AccountIds::resolve` is the one lookup in this repository, reused
    /// rather than a second `getpwnam` written here: it already refuses `root`,
    /// the root group and the system id range, so an FTPS login cannot be
    /// created with a privileged identity even if a name that resolves to one
    /// somehow reached this point.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::AccountMissing`] for every failure of the lookup.
    fn account_ownership(&self, account: &AccountName) -> Result<AccountOwnership, FtpsError> {
        let ids = AccountIds::resolve(account).map_err(|_| FtpsError::AccountMissing)?;

        Ok(AccountOwnership {
            uid: ids.uid(),
            gid: ids.gid(),
        })
    }

    /// Creates `path` with `create_dir_all`, then sets `mode` on it — on the
    /// LEAF, and on nothing else.
    ///
    /// `create_dir_all` makes any missing parent at `0777 & ~umask`, which is
    /// 0755 on both families, and this method does not go back over them. A
    /// caller that needs a parent at a mode of its own asks for that parent in
    /// its own call first; the trait's doc says so, and it says so because it
    /// once said the opposite.
    ///
    /// The mode is set after the fact rather than left to the creation, because
    /// creation applies the process umask and the agent does not control what
    /// its unit file was started with. A jail that came out `0775` because of
    /// an inherited umask is a jail vsftpd refuses to serve a login out of, and
    /// the failure would appear as a login that is disconnected rather than as
    /// anything visible here.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::JailFailed`] when the directory cannot be created
    /// or its mode cannot be set.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), FtpsError> {
        std::fs::create_dir_all(path).map_err(|_| FtpsError::JailFailed)?;
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(mode))
            .map_err(|_| FtpsError::JailFailed)?;

        Ok(())
    }

    /// Lists `account`'s FTPS logins by reading the host's own passwd file.
    ///
    /// The file's TEXT is turned into rows by [`system_accounts`], which is
    /// also what the SFTP area and the monitoring area read it with: where a
    /// home field sits in a passwd line is a question about the host and not
    /// about FTPS. What stays here is the only part that IS about FTPS — which
    /// of those rows is a login of this account, decided by the home field
    /// being exactly this account's FTPS jail and by the name decoding through
    /// the constructor that built it.
    ///
    /// The file rather than a `getpwent` walk, the same deliberate narrowing
    /// the SFTP area makes: enumerating through libc means holding iterator
    /// state across a root process's threads, whereas every login this panel
    /// creates is a local entry in this one file. What is given up is
    /// visibility of logins served by LDAP or another name service — which this
    /// panel never creates, and which it must not delete.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::AccountMissing`] when the passwd file cannot be
    /// read. An account with no logins is an empty list.
    fn account_logins(
        &self,
        passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<FtpsUserName>, FtpsError> {
        let passwd =
            std::fs::read_to_string(passwd_database).map_err(|_| FtpsError::AccountMissing)?;

        let mut logins: Vec<FtpsUserName> = system_accounts(&passwd)
            .into_iter()
            .filter(|row| row.home == jail_directory)
            .filter_map(|row| FtpsUserName::decode(account, &row.name))
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
    /// Returns [`FtpsError::JailFailed`] for every other failure.
    fn remove_file(&self, path: &Path) -> Result<(), FtpsError> {
        match std::fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(_) => Err(FtpsError::JailFailed),
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
    /// Returns [`FtpsError::JailFailed`] for every other failure, including a
    /// directory that is not empty and a mount point that is still mounted.
    fn remove_directory(&self, path: &Path) -> Result<(), FtpsError> {
        match std::fs::remove_dir(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(_) => Err(FtpsError::JailFailed),
        }
    }
}

/// Writes `contents` to a `0600` temporary file under the agent's scratch
/// directory, for a candidate daemon to be pointed at.
///
/// The mode is set on the file before anything is written into it, so the name
/// never points at a file that was briefly wider — the same discipline a private
/// key's write follows, and for the same reason: this text names a private key's
/// path and a certificate's.
///
/// # Errors
///
/// Returns [`FtpsError::SpawnFailed`] with a negative code when the directory or
/// the file cannot be created, its mode cannot be set, or it cannot be written.
fn stage_candidate(contents: &str) -> Result<tempfile::NamedTempFile, FtpsError> {
    let directory = AgentPaths::agent_scratch_dir();
    std::fs::create_dir_all(directory).map_err(|_| FtpsError::program_unavailable())?;

    let mut candidate = tempfile::Builder::new()
        .prefix(".maran-ftps-candidate-")
        .tempfile_in(directory)
        .map_err(|_| FtpsError::program_unavailable())?;
    candidate
        .as_file()
        .set_permissions(std::fs::Permissions::from_mode(0o600))
        .map_err(|_| FtpsError::program_unavailable())?;
    // Written through the handle the mode was set on, not by re-opening the path:
    // between a `set_permissions` and a path-based write there is a window, and
    // the file this stages names a private key.
    candidate
        .as_file_mut()
        .write_all(contents.as_bytes())
        .map_err(|_| FtpsError::program_unavailable())?;

    Ok(candidate)
}
