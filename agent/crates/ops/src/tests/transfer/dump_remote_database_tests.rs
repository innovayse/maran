//! Tests for [`dump_remote_database`], end to end over fake hosts: the order, the
//! cleanup on every path, the typed refusals and the planted-secret hunt.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::Read as _;
use std::sync::Mutex;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::remote_database_name::RemoteDatabaseName;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroFamily, adapter_for};

use super::dump_remote_database;
use crate::transfer::database_loader::DatabaseLoader;
use crate::transfer::fake_transfer_host::{FakeTransferHost, outcome};
use crate::transfer::fake_transfer_work_host::FakeTransferWorkHost;
use crate::transfer::model::dump_request::DumpRequest;
use crate::transfer::model::dump_sink::DumpSink;
use crate::transfer::model::dump_step::DumpStep;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::transfer_fixtures::{PRIVATE_KEY_SENTINEL, credential, endpoint_at};
use crate::transfer::transfer_op_error::TransferOpError;

/// Records the steps reached.
#[derive(Default)]
struct StepSink {
    /// The steps, in order.
    steps: Vec<DumpStep>,
}

impl DumpSink for StepSink {
    fn step(&mut self, step: DumpStep) {
        self.steps.push(step);
    }
}

/// A loader that records what it was given and answers as told.
struct RecordingLoader {
    /// What each load saw: the database and the text of the dump.
    loads: Mutex<Vec<(String, String)>>,
    /// The error every load answers with, if any.
    refusal: Option<fn() -> TransferOpError>,
}

impl RecordingLoader {
    fn accepting() -> Self {
        Self {
            loads: Mutex::new(Vec::new()),
            refusal: None,
        }
    }

    fn refusing(refusal: fn() -> TransferOpError) -> Self {
        Self {
            loads: Mutex::new(Vec::new()),
            refusal: Some(refusal),
        }
    }
}

impl DatabaseLoader for RecordingLoader {
    fn load(&self, database: &DatabaseName, mut dump: File) -> Result<(), TransferOpError> {
        use std::io::Seek as _;
        dump.rewind().unwrap();
        let mut text = String::new();
        dump.read_to_string(&mut text).unwrap();
        self.loads
            .lock()
            .unwrap()
            .push((database.as_str().to_owned(), text));

        match self.refusal {
            Some(refusal) => Err(refusal()),
            None => Ok(()),
        }
    }
}

/// The dump of shop_wp into alice_shop.
fn request() -> DumpRequest {
    DumpRequest {
        endpoint: endpoint_at("source.example.org"),
        credential: credential(),
        source_database: RemoteDatabaseName::parse("shop_wp").unwrap(),
        target_database: DatabaseName::for_account(&AccountName::parse("alice").unwrap(), "shop")
            .unwrap(),
        max_bytes: 1024,
    }
}

/// A source that answers the connection probe.
fn source() -> FakeTransferHost {
    FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(Box::new(|_, _| Ok(outcome(0, "0\n", ""))))
}

fn run(
    host: &FakeTransferHost,
    work: &FakeTransferWorkHost,
    loader: &RecordingLoader,
    sink: &mut StepSink,
) -> Result<crate::transfer::model::dump_summary::DumpSummary, TransferOpError> {
    dump_remote_database(
        host,
        work,
        loader,
        adapter_for(DistroFamily::Debian),
        &request(),
        sink,
    )
}

#[test]
fn a_dump_is_read_then_loaded_into_the_named_database_and_the_file_is_removed() {
    let host = source();
    let work = FakeTransferWorkHost::new().dumping("CREATE TABLE t (id int);\n", 0);
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    let summary = run(&host, &work, &loader, &mut sink).unwrap();

    assert_eq!(summary.dump_bytes, 25);
    assert_eq!(sink.steps, vec![DumpStep::Dumping, DumpStep::Loading]);
    let loads = loader.loads.lock().unwrap();
    assert_eq!(loads.len(), 1);
    assert_eq!(loads[0].0, "alice_shop");
    assert_eq!(loads[0].1, "CREATE TABLE t (id int);\n");
    assert_eq!(work.dumps_created(), work.dumps_removed());
}

#[test]
fn the_dump_command_is_the_closed_one_with_the_validated_name_and_nothing_else() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    run(&host, &work, &loader, &mut sink).unwrap();

    let expected = RemoteCommand::DumpDatabase {
        database: RemoteDatabaseName::parse("shop_wp").unwrap(),
    }
    .render();
    assert_eq!(work.ssh_runs().len(), 1);
    assert_eq!(work.ssh_runs()[0].last().unwrap(), &expected);
    assert!(expected.contains("-- 'shop_wp'"));
    assert!(expected.contains("--single-transaction") && expected.contains("--skip-triggers"));
}

#[test]
fn a_loader_that_refuses_the_dump_leaves_no_file_and_the_refusal_is_the_error() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let loader = RecordingLoader::refusing(|| TransferOpError::LoadRefused);
    let mut sink = StepSink::default();

    let error = run(&host, &work, &loader, &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::LoadRefused));
    assert_eq!(work.dumps_created(), work.dumps_removed());
}

#[test]
fn a_failing_dump_command_is_not_loaded() {
    let host = source();
    let work = FakeTransferWorkHost::new().dumping("partial", 2);
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    let error = run(&host, &work, &loader, &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::DumpFailed { status: 2 }));
    assert!(
        loader.loads.lock().unwrap().is_empty(),
        "a failed dump was loaded"
    );
    assert_eq!(work.dumps_created(), work.dumps_removed());
}

#[test]
fn a_dump_past_the_allowance_is_too_large_and_is_not_loaded() {
    let host = source();
    let work = FakeTransferWorkHost::new().dumping_too_much();
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    let error = run(&host, &work, &loader, &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::DumpTooLarge));
    assert!(loader.loads.lock().unwrap().is_empty());
    assert_eq!(work.dumps_created(), work.dumps_removed());
}

#[test]
fn a_disk_without_room_refuses_before_the_source_is_asked_for_the_dump() {
    let host = source();
    let work = FakeTransferWorkHost::new().without_room();
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    let error = run(&host, &work, &loader, &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::DiskInsufficient));
    assert!(work.ssh_runs().is_empty());
}

#[test]
fn a_changed_host_key_is_the_typed_error_and_nothing_is_dumped() {
    let host = FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(Box::new(|_, _| {
            Ok(outcome(255, "", "Host key verification failed.\n"))
        }));
    let work = FakeTransferWorkHost::new();
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    let error = run(&host, &work, &loader, &mut sink).unwrap_err();

    assert!(matches!(error, TransferOpError::HostKeyChanged));
    assert!(work.dumps_created().is_empty());
    assert!(work.ssh_runs().is_empty());
}

#[test]
fn the_private_key_is_in_no_dump_argument_vector() {
    let host = source();
    let work = FakeTransferWorkHost::new();
    let loader = RecordingLoader::accepting();
    let mut sink = StepSink::default();

    run(&host, &work, &loader, &mut sink).unwrap();

    for vector in work.ssh_runs() {
        for argument in vector {
            assert!(!argument.contains(PRIVATE_KEY_SENTINEL), "{argument}");
        }
    }
    // Positive control: the key was written for the connection (root scratch).
    assert!(
        host.everything_written()
            .iter()
            .any(|(_, content)| String::from_utf8_lossy(content).contains(PRIVATE_KEY_SENTINEL))
    );
}
