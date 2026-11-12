//! Tests for `reconcile_firewall_output_chain`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};
use maran_ops::firewall::{FirewallError, FirewallHost, OutputChainOutcome};

use super::reconcile_firewall_output_chain;

/// A host whose ruleset file is absent or unreadable; it can write nothing.
struct StubHost {
    /// Whether reading the ruleset file fails.
    unreadable: bool,
}

impl FirewallHost for StubHost {
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, FirewallError> {
        panic!("no nft is asked while the ruleset file is absent or unreadable")
    }

    fn read_file(&self, _path: &Path) -> Result<Option<String>, FirewallError> {
        if self.unreadable {
            Err(FirewallError::RulesetUnreadable)
        } else {
            Ok(None)
        }
    }

    fn stage_file(&self, _target: &Path, _contents: &str) -> Result<PathBuf, FirewallError> {
        panic!("nothing is staged")
    }

    fn sync_file(&self, _staged: &Path) -> Result<(), FirewallError> {
        panic!("nothing is flushed")
    }

    fn commit_file(&self, _staged: &Path, _target: &Path) -> Result<(), FirewallError> {
        panic!("nothing is committed")
    }

    fn sync_directory(&self, _target: &Path) -> Result<(), FirewallError> {
        panic!("nothing is flushed")
    }

    fn discard_file(&self, _staged: &Path) {
        panic!("nothing is discarded")
    }
}

/// An unseeded host is an ordinary state: reported, not a failure.
#[test]
fn an_unseeded_host_is_not_a_fault() {
    let outcome = reconcile_firewall_output_chain(
        &StubHost { unreadable: false },
        adapter_for(DistroFamily::Debian),
    );

    assert_eq!(outcome, Some(OutputChainOutcome::NotSeeded));
}

/// A pass that fails answers `None` (and logs at error) and never panics or refuses
/// the start.
#[test]
fn a_host_the_pass_cannot_read_is_reported_and_does_not_stop_the_start() {
    let outcome = reconcile_firewall_output_chain(
        &StubHost { unreadable: true },
        adapter_for(DistroFamily::Debian),
    );

    assert_eq!(outcome, None);
}
