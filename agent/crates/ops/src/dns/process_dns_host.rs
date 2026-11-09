//! The [`DnsHost`] that actually touches this machine.

use std::net::{Ipv4Addr, SocketAddr, TcpStream};
use std::path::Path;
use std::time::{Duration, Instant};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_distro::AdapterBinary;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, SafeWriteError, write_config};

/// How long a connection to port 53 may take. Bounded because the probe runs
/// inside an rpc: a daemon that accepts and stalls must not hold the caller open.
const CONNECT_TIMEOUT: Duration = Duration::from_secs(2);

/// Runs the real daemon's tools and the real service manager, and opens the real
/// socket.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing lives in the
/// operations, where it is tested against a fake. It is the same shape as the
/// FTPS host and not a copy of it — the methods differ (no candidate daemon, no
/// greeting, no jail), so folding the two would delete what each was written for
/// (rules/rust.md "the rule of two").
pub struct ProcessDnsHost {
    /// The instant this host was created, the origin
    /// [`DnsHost::monotonic_now`] measures from.
    origin: Instant,
}

impl ProcessDnsHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            origin: Instant::now(),
        }
    }
}

impl Default for ProcessDnsHost {
    /// The host holds only its clock origin, so the default is the only value
    /// there is.
    fn default() -> Self {
        Self::new()
    }
}

impl ConfigHost for ProcessDnsHost {
    /// Spawns `program` with `arguments` as an argv array, for the config-write
    /// protocol's validator and reload steps. No shell is involved: the
    /// arguments reach `execve` one by one, and `program` is an
    /// [`AdapterBinary`], which only the distro crate can build.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be started
    /// and [`SafeWriteError::Bound`] when it outran its deadline. A non-zero exit
    /// comes back in the outcome for the protocol to judge.
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

impl DnsHost for ProcessDnsHost {
    /// Spawns `program` through the shared bounded spawn body: nothing here pipes
    /// a secret, so the common body loses nothing.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::SpawnFailed`] when the program cannot be started and
    /// [`DnsError::Bound`] when it outran its deadline or output ceiling.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, DnsError> {
        Ok(spawn_argv(program.as_path(), arguments, class)?)
    }

    /// Reads the file, telling "not there" from "there and unreadable".
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigUnreadable`] for every failure other than the
    /// file being absent.
    fn read_config(&self, target: &Path) -> Result<Option<String>, DnsError> {
        match std::fs::read_to_string(target) {
            Ok(contents) => Ok(Some(contents)),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(DnsError::ConfigUnreadable),
        }
    }

    /// Hands the content to the config-write protocol.
    ///
    /// # Errors
    ///
    /// Maps the protocol's failure through `DnsError::from_config_write`.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError> {
        write_config(self, target, contents, validator, reload).map_err(DnsError::from_config_write)
    }

    /// Removes the file; one that is not there is success.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigWrite`] when the file is there and cannot be
    /// removed.
    fn remove_file(&self, path: &Path) -> Result<(), DnsError> {
        match std::fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(DnsError::ConfigWrite {
                reason: error.to_string(),
            }),
        }
    }

    /// Connects to the loopback address on `port`, within `CONNECT_TIMEOUT`.
    fn tcp_port_accepts(&self, port: u16) -> bool {
        let address = SocketAddr::from((Ipv4Addr::LOCALHOST, port));
        TcpStream::connect_timeout(&address, CONNECT_TIMEOUT).is_ok()
    }

    /// Sleeps the calling thread.
    fn pause(&self, duration: Duration) {
        std::thread::sleep(duration);
    }

    /// Time since this host was created.
    fn monotonic_now(&self) -> Duration {
        self.origin.elapsed()
    }
}

#[cfg(test)]
#[path = "../tests/dns/process_dns_host_tests.rs"]
mod tests;
