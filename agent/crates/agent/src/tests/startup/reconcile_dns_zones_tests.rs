//! Tests for `reconcile_dns_zones`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};
use maran_ops::dns::{DnsError, DnsHost, ZoneHost, ZoneReconciliation};
use maran_ops::safe_write::model::{Reload, Validator};

use super::reconcile_dns_zones;

/// A host with one zone file and a daemon that answers nothing, or no readable
/// directory at all.
struct SilentHost {
    /// Whether the zone directory can be listed.
    directory_readable: bool,
}

impl DnsHost for SilentHost {
    fn run(&self, _: &AdapterBinary, _: &[&str], _: CallClass) -> Result<CommandOutcome, DnsError> {
        Ok(CommandOutcome {
            status: 1,
            stdout: String::new(),
            stderr: "rndc: connect failed: connection refused".to_owned(),
        })
    }
    fn read_config(&self, _: &Path) -> Result<Option<String>, DnsError> {
        Ok(None)
    }
    fn write_config(
        &self,
        _: &Path,
        _: &str,
        _: &Validator<'_>,
        _: &Reload<'_>,
    ) -> Result<(), DnsError> {
        Ok(())
    }
    fn remove_file(&self, _: &Path) -> Result<(), DnsError> {
        Ok(())
    }
    fn tcp_port_accepts(&self, _: u16) -> bool {
        false
    }
    fn pause(&self, _: Duration) {}
    fn monotonic_now(&self) -> Duration {
        Duration::ZERO
    }
}

impl ZoneHost for SilentHost {
    fn write_zone_file(
        &self,
        _: &Path,
        _: &str,
        _: &Validator<'_>,
        _: &Reload<'_>,
    ) -> Result<(), DnsError> {
        Ok(())
    }
    fn zone_file_names(&self, _: &Path) -> Result<Vec<String>, DnsError> {
        if self.directory_readable {
            Ok(vec!["example.com.db".to_owned()])
        } else {
            Err(DnsError::ZoneDirectoryUnreadable)
        }
    }
    fn available_bytes(&self, _: &Path) -> std::io::Result<u64> {
        Ok(u64::MAX)
    }
}

#[test]
fn a_host_without_a_running_daemon_is_not_a_fault() {
    let host = SilentHost {
        directory_readable: true,
    };

    assert_eq!(
        reconcile_dns_zones(&host, adapter_for(DistroFamily::Debian)),
        Some(ZoneReconciliation::DaemonNotAnswering)
    );
}

#[test]
fn an_unreadable_zone_directory_is_reported_and_never_stops_the_start() {
    let host = SilentHost {
        directory_readable: false,
    };

    assert_eq!(
        reconcile_dns_zones(&host, adapter_for(DistroFamily::Debian)),
        None
    );
}
