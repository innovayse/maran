//! Tests for [`test_source`], end to end over a fake host, including the
//! planted-secret hunt: the private key reaches no argument vector, no error
//! text and no file that outlives the call.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_distro::{DistroFamily, adapter_for};

use super::test_source;
use crate::transfer::fake_transfer_host::{FakeTransferHost, RecordedRun, Responder, outcome};
use crate::transfer::model::source_kind::SourceKind;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::transfer_fixtures::{
    PRIVATE_KEY, PRIVATE_KEY_SENTINEL, credential, endpoint_at,
};
use crate::transfer::transfer_op_error::TransferOpError;

/// The stdout a healthy Ubuntu source prints for the probe in `run`.
fn healthy_answer(run: &RecordedRun) -> String {
    let command = run.arguments.last().unwrap();
    let render = |probe: RemoteCommand| probe.render();

    if *command == render(RemoteCommand::EffectiveUid) {
        "0\n".to_owned()
    } else if *command == render(RemoteCommand::HomeFilesystemUsage) {
        "4096 1000 400\n".to_owned()
    } else if *command == render(RemoteCommand::OsRelease) {
        "PRETTY_NAME=\"AlmaLinux 9.4\"\n".to_owned()
    } else if *command == render(RemoteCommand::WebServerVersion) {
        "Server version: Apache/2.4.57\n".to_owned()
    } else if *command == render(RemoteCommand::MailStorePresent) {
        "yes\n".to_owned()
    } else {
        String::new()
    }
}

/// A host for a source at a public address, answering as `responder`.
fn host_answering(responder: Responder) -> FakeTransferHost {
    FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(responder)
}

/// Runs the test and returns the host and the error text it produced.
fn failure_of(host: &FakeTransferHost) -> (TransferOpError, String) {
    let error = test_source(
        host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap_err();
    let text = format!("{error} {error:?}");

    (error, text)
}

#[test]
fn a_healthy_source_is_described_on_every_family() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel, DistroFamily::Suse] {
        let host = host_answering(Box::new(|_, run| Ok(outcome(0, &healthy_answer(run), ""))));

        let tested = test_source(
            &host,
            adapter_for(family),
            &endpoint_at("source.example.org"),
            &credential(),
        )
        .unwrap();

        assert!(tested.is_root);
        assert_eq!(tested.system_release.as_deref(), Some("AlmaLinux 9.4"));
        assert_eq!(tested.kind, SourceKind::GenericLinux);
        assert!(tested.mail_found);
        assert_eq!(host.runs().len(), 7, "one ssh run per probe");
        assert_eq!(host.removed(), host.created());
    }
}

#[test]
fn a_changed_host_key_is_the_typed_error_and_nothing_is_retried() {
    let host = host_answering(Box::new(|_, _| {
        Ok(outcome(
            255,
            "",
            "@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @\nHost key verification failed.\n",
        ))
    }));

    let (error, _) = failure_of(&host);

    assert!(matches!(error, TransferOpError::HostKeyChanged));
    assert_eq!(
        host.runs().len(),
        1,
        "a changed host key was retried or probing went on"
    );
}

#[test]
fn a_refused_key_and_an_unreachable_source_are_told_apart() {
    let refused = host_answering(Box::new(|_, _| {
        Ok(outcome(
            255,
            "",
            "root@198.51.100.20: Permission denied (publickey).\n",
        ))
    }));
    let silent = host_answering(Box::new(|_, _| {
        Ok(outcome(
            255,
            "",
            "ssh: connect to host 198.51.100.20 port 22: Connection timed out\n",
        ))
    }));

    assert!(matches!(
        failure_of(&refused).0,
        TransferOpError::AuthRefused
    ));
    assert!(matches!(
        failure_of(&silent).0,
        TransferOpError::Unreachable
    ));
}

#[test]
fn a_forbidden_address_stops_before_any_file_or_packet() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["169.254.169.254"]);

    let (error, _) = failure_of(&host);

    assert!(matches!(error, TransferOpError::AddressRefused));
    assert!(host.runs().is_empty() && host.created().is_empty());
}

/// Every way the call can fail, each as a host that produces it.
fn failing_hosts() -> Vec<(&'static str, FakeTransferHost)> {
    let sentinel_stderr = format!("Permission denied (publickey). {PRIVATE_KEY_SENTINEL}\n");
    let hostile_stderr = format!("Host key verification failed. {PRIVATE_KEY_SENTINEL}\n");

    vec![
        (
            "address refused",
            FakeTransferHost::new().resolving("source.example.org", &["127.0.0.1"]),
        ),
        (
            "host key changed",
            host_answering(Box::new(move |_, _| Ok(outcome(255, "", &hostile_stderr)))),
        ),
        (
            "auth refused",
            host_answering(Box::new(move |_, _| Ok(outcome(255, "", &sentinel_stderr)))),
        ),
        (
            "unreachable",
            host_answering(Box::new(|_, _| {
                Ok(outcome(255, "", "Connection refused\n"))
            })),
        ),
        (
            "bound",
            host_answering(Box::new(|_, _| {
                Err(TransferOpError::OutputBound(BoundExceeded::TimedOut {
                    program: "/usr/bin/ssh".to_owned(),
                    deadline: Duration::from_secs(900),
                }))
            })),
        ),
        (
            "spawn",
            host_answering(Box::new(|_, _| {
                Err(TransferOpError::Spawn(SpawnError::Start {
                    program: "/usr/bin/ssh".to_owned(),
                    reason: "not found".to_owned(),
                }))
            })),
        ),
        (
            "invalid answer",
            host_answering(Box::new(|_, _| Ok(outcome(0, "root\n", "")))),
        ),
        ("scratch unavailable", {
            let host = host_answering(Box::new(|_, _| Ok(outcome(0, "", ""))));
            host.put_file(std::path::Path::new("/scratch/00/key"), "squatter");
            host
        }),
        ("unresolvable", FakeTransferHost::new()),
    ]
}

#[test]
fn the_private_key_is_in_no_argument_vector_error_text_or_surviving_file() {
    for (name, host) in failing_hosts() {
        let (_, text) = failure_of(&host);

        for run in host.runs() {
            for argument in &run.arguments {
                assert!(
                    !argument.contains(PRIVATE_KEY_SENTINEL),
                    "{name}: argv holds the key"
                );
                assert!(
                    !argument.contains("BEGIN OPENSSH PRIVATE KEY"),
                    "{name}: argv holds the key"
                );
            }
            assert!(!format!("{:?}", run.program).contains(PRIVATE_KEY_SENTINEL));
        }
        assert!(
            !text.contains(PRIVATE_KEY_SENTINEL),
            "{name}: error text holds the key"
        );
        assert!(
            !text.contains("PRIVATE KEY"),
            "{name}: error text holds the key"
        );
        for (path, content) in host.files() {
            assert!(
                !String::from_utf8_lossy(&content).contains(PRIVATE_KEY_SENTINEL),
                "{name}: {path:?} outlived the call holding the key"
            );
        }
        assert_eq!(
            host.removed(),
            host.created(),
            "{name}: a scratch directory was left"
        );
    }
}

#[test]
fn the_secret_hunt_can_find_a_planted_key_so_its_silence_means_something() {
    // Positive control: on a healthy run the key IS written, once, to the key
    // file, and the same hunt finds it there. The credential's Debug does not.
    let host = host_answering(Box::new(|_, run| Ok(outcome(0, &healthy_answer(run), ""))));
    test_source(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap();

    let found: Vec<_> = host
        .everything_written()
        .into_iter()
        .filter(|(_, content)| String::from_utf8_lossy(content).contains(PRIVATE_KEY_SENTINEL))
        .collect();
    assert_eq!(found.len(), 1, "the key must be written exactly once");
    assert_eq!(found[0].1, PRIVATE_KEY.as_bytes());
    assert!(!format!("{:?}", credential()).contains(PRIVATE_KEY_SENTINEL));

    // And the key file existed while the client ran, with the key in it.
    assert!(host.runs().iter().all(|run| {
        run.files
            .values()
            .any(|content| content == PRIVATE_KEY.as_bytes())
    }));
}
