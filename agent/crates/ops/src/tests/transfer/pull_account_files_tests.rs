//! Tests for [`pull_account_files`], end to end over fake hosts, including the
//! planted-secret hunt: the private key reaches no argument vector and the key
//! directories do not outlive the call.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroFamily, adapter_for};

use super::pull_account_files;
use crate::transfer::fake_transfer_host::{FakeTransferHost, outcome};
use crate::transfer::fake_transfer_work_host::FakeTransferWorkHost;
use crate::transfer::model::pull_phase::PullPhase;
use crate::transfer::model::pull_request::PullRequest;
use crate::transfer::model::pull_sink::PullSink;
use crate::transfer::model::rsync_slice_outcome::RsyncSliceOutcome;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::transfer_fixtures::{PRIVATE_KEY_SENTINEL, credential, endpoint_at};
use crate::transfer::transfer_op_error::TransferOpError;

/// A sink that counts what it is told and can be closed.
struct CountingSink {
    /// The counts reported.
    reported: Vec<u32>,
    /// Whether the client is still there.
    open: bool,
}

impl CountingSink {
    fn open() -> Self {
        Self {
            reported: Vec::new(),
            open: true,
        }
    }
}

impl PullSink for CountingSink {
    fn progress(&mut self, slices_completed: u32) {
        self.reported.push(slices_completed);
    }

    fn is_open(&self) -> bool {
        self.open
    }
}

/// A pull of alice's home from a public source.
fn request(phase: PullPhase) -> PullRequest {
    PullRequest {
        endpoint: endpoint_at("source.example.org"),
        credential: credential(),
        account: AccountName::parse("alice").unwrap(),
        source_home: RemotePath::parse("/home/alice").unwrap(),
        phase,
        bandwidth: None,
        expected_bytes: 4096,
    }
}

/// A transfer host for a public source that answers the size probe.
fn source() -> FakeTransferHost {
    let probe = RemoteCommand::HomeDiskUsage {
        home: RemotePath::parse("/home/alice").unwrap(),
    }
    .render();

    FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(Box::new(move |_, run| {
            if *run.arguments.last().unwrap() == probe {
                Ok(outcome(0, "8\t/home/alice\n", ""))
            } else {
                Ok(outcome(0, "", ""))
            }
        }))
}

fn run(
    host: &FakeTransferHost,
    work: &FakeTransferWorkHost,
    request: &PullRequest,
    sink: &mut CountingSink,
) -> Result<crate::transfer::model::pull_summary::PullSummary, TransferOpError> {
    pull_account_files(host, work, adapter_for(DistroFamily::Debian), request, sink)
}

#[test]
fn a_pull_that_finishes_in_one_run_reports_one_slice_and_the_measured_size() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    let summary = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap();

    assert_eq!(summary.slices, 1);
    assert_eq!(summary.source_bytes, Some(8 * 1024));
    assert_eq!(work.slices().len(), 1);
    assert_eq!(work.slices()[0].account, "alice");
}

#[test]
fn a_run_that_reaches_its_deadline_is_followed_by_another_and_each_is_reported() {
    let host = source();
    let work = FakeTransferWorkHost::new().running(vec![
        RsyncSliceOutcome::SliceEnded,
        RsyncSliceOutcome::SliceEnded,
        RsyncSliceOutcome::Finished,
    ]);
    let mut sink = CountingSink::open();

    let summary = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap();

    assert_eq!(summary.slices, 3);
    assert_eq!(sink.reported, vec![1, 2]);
}

#[test]
fn the_free_space_is_asked_before_anything_is_written_and_a_refusal_writes_nothing() {
    let host = source();
    let work = FakeTransferWorkHost::new().without_room();
    let mut sink = CountingSink::open();

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::DiskInsufficient));
    assert_eq!(work.room_checks(), vec![("alice".to_owned(), 4096)]);
    assert!(work.receivers_created().is_empty());
    assert!(
        host.runs().is_empty(),
        "the source was contacted before the room was known"
    );
}

#[test]
fn the_final_phase_asks_for_the_floor_alone() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    run(&host, &work, &request(PullPhase::Final), &mut sink).unwrap();

    assert_eq!(work.room_checks(), vec![("alice".to_owned(), 0)]);
}

#[test]
fn a_failing_status_is_a_pull_failure_that_keeps_its_number() {
    let host = source();
    let work = FakeTransferWorkHost::new().running(vec![RsyncSliceOutcome::Failed { status: 23 }]);
    let mut sink = CountingSink::open();

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::PullFailed { status: 23 }));
}

#[test]
fn the_receivers_own_write_failing_is_a_disk_failure() {
    let host = source();
    let work = FakeTransferWorkHost::new().running(vec![RsyncSliceOutcome::Failed { status: 11 }]);
    let mut sink = CountingSink::open();

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::DiskInsufficient));
}

#[test]
fn a_client_that_left_stops_the_pull_before_the_next_run_and_keeps_the_partial_state() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();
    sink.open = false;

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::Cancelled));
    assert!(
        work.slices().is_empty(),
        "no run was started for a client that left"
    );
}

#[test]
fn the_key_is_written_once_to_the_receiver_and_removed_on_success_and_on_failure() {
    for outcomes in [vec![], vec![RsyncSliceOutcome::Failed { status: 12 }]] {
        let host = source();
        let work = FakeTransferWorkHost::new().running(outcomes);
        let mut sink = CountingSink::open();

        let _ = run(&host, &work, &request(PullPhase::Initial), &mut sink);

        let written = work.receiver_files();
        assert_eq!(written.len(), 2, "the key and the known_hosts file");
        assert!(
            written
                .iter()
                .any(|(_, content)| String::from_utf8_lossy(content).contains(PRIVATE_KEY_SENTINEL)),
            "positive control: the key was written to the receiver directory"
        );
        assert_eq!(work.receivers_created(), work.receivers_removed());
        // The root-owned copy used for the size probe is gone too.
        assert_eq!(host.created().len(), host.removed().len());
    }
}

#[test]
fn the_key_is_in_no_argument_vector() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap();

    for slice in work.slices() {
        for argument in &slice.arguments {
            assert!(!argument.contains(PRIVATE_KEY_SENTINEL), "{argument}");
        }
    }
    for run in host.runs() {
        for argument in &run.arguments {
            assert!(!argument.contains(PRIVATE_KEY_SENTINEL), "{argument}");
        }
    }
}

#[test]
fn every_run_of_rsync_is_given_the_hundred_second_slice_deadline() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap();

    assert_eq!(work.slices()[0].deadline, Duration::from_secs(100));
}

#[test]
fn the_rsync_binary_is_the_adapters_and_the_transport_pins_the_host_key() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap();

    let slice = &work.slices()[0];
    assert_eq!(
        slice.program,
        adapter_for(DistroFamily::Debian).rsync_binary().as_path()
    );
    assert!(
        slice
            .arguments
            .iter()
            .any(|argument| argument.starts_with("--rsh=")
                && argument.contains("StrictHostKeyChecking=yes"))
    );
}

#[test]
fn a_forbidden_address_is_refused_before_the_account_is_touched() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["127.0.0.1"]);
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::AddressRefused));
    assert!(work.receivers_created().is_empty());
    assert!(work.slices().is_empty());
}

#[test]
fn a_changed_host_key_in_the_size_probe_stops_the_pull_before_any_copy() {
    let host = FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(Box::new(|_, _| {
            Ok(outcome(255, "", "Host key verification failed.\n"))
        }));
    let work = FakeTransferWorkHost::new();
    let mut sink = CountingSink::open();

    let error = run(&host, &work, &request(PullPhase::Initial), &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::HostKeyChanged));
    assert!(work.slices().is_empty());
}
