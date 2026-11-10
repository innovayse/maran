//! Tests for [`SshSourceShell`], driven over a fake host that records the argv.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use super::SOURCE_OUTPUT_LIMIT_BYTES;
use crate::transfer::fake_transfer_host::{FakeTransferHost, Responder, outcome};
use crate::transfer::open_source_shell::open_source_shell;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::transfer_fixtures::{credential, endpoint_at};
use crate::transfer::transfer_op_error::TransferOpError;

/// A host for a source at a public address whose programs answer as `responder`.
fn host_answering(responder: Responder) -> FakeTransferHost {
    FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(responder)
}

#[test]
fn a_command_runs_through_the_families_ssh_with_the_rendered_text_and_the_read_class() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel, DistroFamily::Suse] {
        let distro = adapter_for(family);
        let host = host_answering(Box::new(|_, _| Ok(outcome(0, "1000\n", ""))));
        let shell = open_source_shell(
            &host,
            distro,
            &endpoint_at("source.example.org"),
            &credential(),
        )
        .unwrap();

        let answer = shell.run(&RemoteCommand::EffectiveUid).unwrap();

        assert_eq!(answer.stdout, "1000\n");
        let runs = host.runs();
        assert_eq!(runs.len(), 1);
        assert_eq!(runs[0].program, distro.ssh_binary().as_path());
        assert_eq!(
            runs[0].arguments.last().unwrap(),
            &RemoteCommand::EffectiveUid.render()
        );
        assert_eq!(runs[0].class, CallClass::SourceRead);
        assert_eq!(runs[0].stdout_cap, SOURCE_OUTPUT_LIMIT_BYTES);
        assert_eq!(SOURCE_OUTPUT_LIMIT_BYTES, 1024 * 1024);
    }
}

#[test]
fn the_connection_is_made_to_the_resolved_address_and_not_to_the_name() {
    let host = host_answering(Box::new(|_, _| Ok(outcome(0, "", ""))));
    let shell = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap();

    shell.run(&RemoteCommand::ListPasswd).unwrap();
    shell.run(&RemoteCommand::ListPasswd).unwrap();

    for run in host.runs() {
        assert!(run.arguments.contains(&"root@198.51.100.20".to_owned()));
        assert!(
            !run.arguments
                .iter()
                .any(|argument| argument.contains("source.example.org"))
        );
    }
    assert_eq!(host.resolutions(), 1, "the name was resolved again");
}

#[test]
fn a_client_failure_is_classified_from_its_own_words() {
    for (stderr, expected) in [
        ("Host key verification failed.\n", "HostKeyChanged"),
        (
            "root@198.51.100.20: Permission denied (publickey).\n",
            "AuthRefused",
        ),
        (
            "ssh: connect to host 198.51.100.20 port 22: Connection refused\n",
            "Unreachable",
        ),
    ] {
        let message = stderr.to_owned();
        let host = host_answering(Box::new(move |_, _| Ok(outcome(255, "", &message))));
        let shell = open_source_shell(
            &host,
            adapter_for(DistroFamily::Rhel),
            &endpoint_at("source.example.org"),
            &credential(),
        )
        .unwrap();

        let error = shell.run(&RemoteCommand::EffectiveUid).unwrap_err();

        assert_eq!(format!("{error:?}"), expected, "{stderr}");
    }
}

#[test]
fn a_remote_command_that_exits_non_zero_is_an_outcome_and_not_an_error() {
    let host = host_answering(Box::new(|_, _| {
        Ok(outcome(1, "", "no crontab for alice\n"))
    }));
    let shell = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap();

    let answer = shell.run(&RemoteCommand::ListPasswd).unwrap();

    assert_eq!(answer.status, 1);
}

#[test]
fn a_deadline_or_cap_is_an_error_and_never_an_empty_answer() {
    let host = host_answering(Box::new(|_, _| {
        Err(TransferOpError::OutputBound(BoundExceeded::TimedOut {
            program: "/usr/bin/ssh".to_owned(),
            deadline: Duration::from_secs(900),
        }))
    }));
    let shell = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap();

    assert!(matches!(
        shell.run(&RemoteCommand::ListPasswd),
        Err(TransferOpError::OutputBound(_))
    ));
}

#[test]
fn dropping_the_shell_removes_the_key_directory() {
    let host = host_answering(Box::new(|_, _| Ok(outcome(0, "", ""))));
    {
        let _shell = open_source_shell(
            &host,
            adapter_for(DistroFamily::Debian),
            &endpoint_at("source.example.org"),
            &credential(),
        )
        .unwrap();
        assert!(
            host.removed().is_empty(),
            "removed while the shell was alive"
        );
        assert_eq!(host.files().len(), 2);
    }

    assert_eq!(host.removed(), host.created());
    assert!(host.files().is_empty());
}
