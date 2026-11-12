//! The [`MailHost`] that actually touches this machine.

use std::path::Path;
use std::time::{Duration, Instant};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::read_host_addresses::read_host_addresses;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::AdapterBinary;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::path_facts::PathFacts;
use crate::mail::parse_listening_sockets::parse_listening_sockets;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError, write_config_set, write_unreloaded_config};
use crate::ssl::{CertificateState, ProcessSslHost, certificate_state};

/// The kernel's table of IPv4 TCP sockets.
const TCP_TABLE: &str = "/proc/net/tcp";

/// The kernel's table of IPv6 TCP sockets; absent when IPv6 is disabled.
const TCP6_TABLE: &str = "/proc/net/tcp6";

/// The most a socket table is read up to. A busy host has thousands of lines of
/// about 150 bytes; a table past this is not one the agent should hold in memory.
const TABLE_READ_LIMIT: u64 = 16 * 1024 * 1024;

/// Runs the real mail tools and the real service manager, and reads the real
/// kernel tables.
///
/// The only implementation that touches the machine, and deliberately the smallest
/// piece of the area: every decision worth reviewing lives in the operations,
/// where it is tested against a fake. What is left here is spawning, reading a
/// file, and handing content to the config-write protocol.
pub struct ProcessMailHost {
    /// The SSL area's real host, which the certificate question is delegated to:
    /// mail reads the store through the code that owns it and never writes to it.
    ssl: ProcessSslHost,
    /// The instant this host was created, the origin of [`MailHost::monotonic_now`].
    origin: Instant,
}

impl ProcessMailHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            ssl: ProcessSslHost::new(),
            origin: Instant::now(),
        }
    }
}

impl Default for ProcessMailHost {
    /// The host holds only its delegate, so the default is the only value there is.
    fn default() -> Self {
        Self::new()
    }
}

/// Reads one socket table, bounded.
///
/// # Errors
///
/// Returns the I/O error unchanged, so the caller can tell a missing table (IPv6
/// disabled) from an unreadable one.
fn read_table(path: &str) -> std::io::Result<String> {
    use std::io::Read as _;

    let mut text = String::new();
    std::fs::File::open(path)?
        .take(TABLE_READ_LIMIT)
        .read_to_string(&mut text)?;
    Ok(text)
}

impl ConfigHost for ProcessMailHost {
    /// Spawns `program` with `arguments` as an argv array, for the config-write
    /// protocol's validator and reload steps.
    ///
    /// No shell is involved (rules/security.md item 3). `program` is an
    /// [`AdapterBinary`], which only the distro crate can build.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be started
    /// and [`SafeWriteError::Bound`] when it outran its deadline or output
    /// ceiling. A non-zero exit comes back in the outcome for the protocol to
    /// judge.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        spawn_argv(program.as_path(), arguments, class)
            .map_err(|error| SafeWriteError::from_spawn(program, error))
    }
}

impl MailHost for ProcessMailHost {
    /// Spawns `program` with `arguments` as an argv array and waits for it, for at
    /// most the deadline of `class`. Nothing here pipes a secret, so the shared
    /// bounded spawn body loses nothing.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::SpawnFailed`] with a negative code when the program
    /// cannot be started, and [`MailError::Bound`] when it outran its deadline or
    /// output ceiling.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, MailError> {
        spawn_argv(program.as_path(), arguments, class).map_err(MailError::from)
    }

    /// Reads the live file, `None` when it is absent.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when it exists and cannot be read.
    fn read_config(&self, target: &Path) -> Result<Option<String>, MailError> {
        match std::fs::read_to_string(target) {
            Ok(contents) => Ok(Some(contents)),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(MailError::ConfigUnreadable),
        }
    }

    /// Owner and mode of `path`, `None` when it is absent.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when it exists and cannot be inspected.
    fn path_facts(&self, path: &Path) -> Result<Option<PathFacts>, MailError> {
        use std::os::unix::fs::MetadataExt;

        match std::fs::symlink_metadata(path) {
            Ok(metadata) => Ok(Some(PathFacts {
                mode: metadata.mode() & 0o7777,
                uid: metadata.uid(),
                gid: metadata.gid(),
            })),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(MailError::ConfigUnreadable),
        }
    }

    /// Hands the set to the one implementation of the config-write protocol and
    /// translates its typed failure into this area's. Adds nothing of its own.
    ///
    /// # Errors
    ///
    /// As [`MailHost::write_config_set`].
    fn write_config_set(
        &self,
        daemon: MailDaemon,
        unit: &str,
        files: &[ConfigFile<'_>],
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), MailError> {
        write_config_set(self, files, validator, reload)
            .map_err(|error| MailError::from_config_write(error, daemon, unit))
    }

    /// Hands one file to the protocol's no-reload variant.
    ///
    /// # Errors
    ///
    /// As [`MailHost::write_file_unreloaded`].
    fn write_file_unreloaded(
        &self,
        daemon: MailDaemon,
        unit: &str,
        file: &ConfigFile<'_>,
        validator: &Validator<'_>,
    ) -> Result<(), MailError> {
        write_unreloaded_config(self, file, validator)
            .map_err(|error| MailError::from_config_write(error, daemon, unit))
    }

    /// Whether `path` exists, `false` for a path that cannot be inspected.
    fn path_exists(&self, path: &Path) -> bool {
        path.exists()
    }

    /// Reads both kernel socket tables and parses the listening rows.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::SocketsUnreadable`] when the IPv4 table cannot be
    /// read, or when the IPv6 table exists and cannot be read. A missing IPv6
    /// table is a host with IPv6 disabled.
    fn listening_sockets(&self) -> Result<Vec<ListeningSocket>, MailError> {
        let v4 = read_table(TCP_TABLE).map_err(|_| MailError::SocketsUnreadable)?;
        let v6 = match read_table(TCP6_TABLE) {
            Ok(text) => text,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
            Err(_) => return Err(MailError::SocketsUnreadable),
        };
        Ok(parse_listening_sockets(&v4, &v6))
    }

    /// Reads the kernel's address tables.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::HostAddressesUnreadable`] when either table that
    /// exists cannot be read.
    fn host_addresses(&self) -> Result<HostAddresses, MailError> {
        read_host_addresses().map_err(MailError::HostAddressesUnreadable)
    }

    /// Answers what certificate material is installed for `domain`.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when a file of the material exists
    /// and cannot be read.
    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, MailError> {
        certificate_state(&self.ssl, domain).map_err(|_| MailError::ConfigUnreadable)
    }

    /// Sleeps the calling thread.
    fn pause(&self, duration: Duration) {
        std::thread::sleep(duration);
    }

    /// Time since this host was created, from the monotonic clock.
    fn monotonic_now(&self) -> Duration {
        self.origin.elapsed()
    }
}

#[cfg(test)]
#[path = "../tests/mail/process_mail_host_tests.rs"]
mod tests;
