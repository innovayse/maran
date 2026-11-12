//! Tests for `reconcile_mail_maps`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};
use maran_ops::mail::{ListeningSocket, MailDaemon, MailError, MailHost, PathFacts};
use maran_ops::safe_write::model::config_file::ConfigFile;
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::ssl::CertificateState;

use super::reconcile_mail_maps;

/// A host that answers only the one read the pass starts with: no file at all, or a
/// read that fails.
struct StubHost {
    /// Whether reading the accounts map fails.
    unreadable: bool,
}

impl MailHost for StubHost {
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, MailError> {
        panic!("nothing is run while mail is not enabled")
    }

    fn read_config(&self, _target: &Path) -> Result<Option<String>, MailError> {
        if self.unreadable {
            Err(MailError::ConfigUnreadable)
        } else {
            Ok(None)
        }
    }

    fn path_facts(&self, _path: &Path) -> Result<Option<PathFacts>, MailError> {
        panic!("no path is inspected")
    }

    fn write_config_set(
        &self,
        _daemon: MailDaemon,
        _unit: &str,
        _files: &[ConfigFile<'_>],
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), MailError> {
        panic!("nothing is written")
    }

    fn write_file_unreloaded(
        &self,
        _daemon: MailDaemon,
        _unit: &str,
        _file: &ConfigFile<'_>,
        _validator: &Validator<'_>,
    ) -> Result<(), MailError> {
        panic!("nothing is written while mail is not enabled")
    }

    fn path_exists(&self, _path: &Path) -> bool {
        panic!("no path is probed")
    }

    fn listening_sockets(&self) -> Result<Vec<ListeningSocket>, MailError> {
        panic!("no socket is listed")
    }

    fn host_addresses(&self) -> Result<HostAddresses, MailError> {
        panic!("no address is read")
    }

    fn certificate_state(&self, _domain: &Domain) -> Result<CertificateState, MailError> {
        panic!("no certificate is read")
    }

    fn pause(&self, _duration: Duration) {}

    fn monotonic_now(&self) -> Duration {
        Duration::ZERO
    }
}

/// A host that never enabled mail is an ordinary state: nothing is written.
#[test]
fn a_host_without_mail_is_left_alone() {
    let outcome = reconcile_mail_maps(
        &StubHost { unreadable: false },
        adapter_for(DistroFamily::Debian),
    );

    assert_eq!(outcome, Some(false));
}

/// A pass that fails answers `None` (and logs at error) and never panics or refuses
/// the start.
#[test]
fn a_host_the_pass_cannot_read_is_reported_and_does_not_stop_the_start() {
    let outcome = reconcile_mail_maps(
        &StubHost { unreadable: true },
        adapter_for(DistroFamily::Debian),
    );

    assert_eq!(outcome, None);
}
