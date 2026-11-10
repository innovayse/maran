//! Tests for [`probe_source`] against a source shell that answers per command.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;

use super::probe_source;
use crate::transfer::fake_transfer_host::outcome;
use crate::transfer::model::source_database_server::SourceDatabaseServer;
use crate::transfer::model::source_disk::SourceDisk;
use crate::transfer::model::source_kind::SourceKind;
use crate::transfer::model::source_web_server::SourceWebServer;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::transfer_op_error::TransferOpError;

/// What a probe answers: an outcome, or a failure of the connection.
type Answer = Result<CommandOutcome, fn() -> TransferOpError>;

/// A source that answers each probe from a table and records what was asked.
struct ScriptedSource {
    /// The answer for a command, by its variant.
    answers: Vec<(RemoteCommand, Answer)>,
    /// The commands asked, in order.
    asked: Mutex<Vec<RemoteCommand>>,
}

impl ScriptedSource {
    /// A source that answers like a healthy Ubuntu host with nginx, MariaDB and
    /// no mail, run by root.
    fn healthy() -> Self {
        Self {
            answers: vec![
                (RemoteCommand::EffectiveUid, Ok(outcome(0, "0\n", ""))),
                (
                    RemoteCommand::HomeFilesystemUsage,
                    Ok(outcome(0, "4096 1000 400\n", "")),
                ),
                (
                    RemoteCommand::OsRelease,
                    Ok(outcome(0, "PRETTY_NAME=\"Ubuntu 24.04 LTS\"\n", "")),
                ),
                (
                    RemoteCommand::DatabaseServerVersion,
                    Ok(outcome(0, "mysql from 11.4.2-MariaDB, client 15.2\n", "")),
                ),
                (
                    RemoteCommand::WebServerVersion,
                    Ok(outcome(0, "nginx version: nginx/1.24.0\n", "")),
                ),
                (RemoteCommand::MailStorePresent, Ok(outcome(0, "no\n", ""))),
                (RemoteCommand::MaranPanelVersion, Ok(outcome(1, "", ""))),
            ],
            asked: Mutex::new(Vec::new()),
        }
    }

    /// Replaces the answer for `command`.
    fn with(mut self, command: RemoteCommand, answer: Answer) -> Self {
        for entry in &mut self.answers {
            if entry.0 == command {
                entry.1 = answer.clone();
            }
        }
        self
    }
}

impl SourceShell for ScriptedSource {
    fn run(&self, command: &RemoteCommand) -> Result<CommandOutcome, TransferOpError> {
        self.asked.lock().unwrap().push(command.clone());
        let answer = self
            .answers
            .iter()
            .find(|entry| entry.0 == *command)
            .map(|entry| entry.1.clone())
            .unwrap_or_else(|| panic!("an unexpected command {command:?}"));

        answer.map_err(|failure| failure())
    }
}

#[test]
fn a_healthy_generic_source_is_described_field_by_field() {
    let tested = probe_source(&ScriptedSource::healthy()).unwrap();

    assert!(tested.is_root);
    assert_eq!(
        tested.home_disk,
        Some(SourceDisk {
            free_bytes: 400 * 4096,
            total_bytes: 1000 * 4096,
        })
    );
    assert_eq!(tested.system_release.as_deref(), Some("Ubuntu 24.04 LTS"));
    assert_eq!(tested.kind, SourceKind::GenericLinux);
    assert_eq!(tested.maran_version, None);
    assert_eq!(tested.database_server, Some(SourceDatabaseServer::Mariadb));
    assert_eq!(tested.web_server, Some(SourceWebServer::Nginx));
    assert!(!tested.mail_found);
}

#[test]
fn the_probes_run_in_the_documented_order_and_only_the_probes_run() {
    let source = ScriptedSource::healthy();

    probe_source(&source).unwrap();

    assert_eq!(
        *source.asked.lock().unwrap(),
        vec![
            RemoteCommand::EffectiveUid,
            RemoteCommand::HomeFilesystemUsage,
            RemoteCommand::OsRelease,
            RemoteCommand::DatabaseServerVersion,
            RemoteCommand::WebServerVersion,
            RemoteCommand::MailStorePresent,
            RemoteCommand::MaranPanelVersion,
        ]
    );
}

#[test]
fn an_unprivileged_login_is_not_root() {
    let source =
        ScriptedSource::healthy().with(RemoteCommand::EffectiveUid, Ok(outcome(0, "1000\n", "")));

    assert!(!probe_source(&source).unwrap().is_root);
}

#[test]
fn a_release_state_file_makes_the_source_a_maran_panel() {
    let source = ScriptedSource::healthy().with(
        RemoteCommand::MaranPanelVersion,
        Ok(outcome(0, "VERSION=1.0.0-beta.4\n", "")),
    );

    let tested = probe_source(&source).unwrap();

    assert_eq!(tested.kind, SourceKind::Maran);
    assert_eq!(tested.maran_version.as_deref(), Some("1.0.0-beta.4"));
}

#[test]
fn mail_found_is_reported() {
    let source = ScriptedSource::healthy()
        .with(RemoteCommand::MailStorePresent, Ok(outcome(0, "yes\n", "")));

    assert!(probe_source(&source).unwrap().mail_found);
}

#[test]
fn a_probe_that_did_not_answer_leaves_its_field_unknown() {
    let source = ScriptedSource::healthy()
        .with(RemoteCommand::HomeFilesystemUsage, Ok(outcome(1, "", "")))
        .with(RemoteCommand::OsRelease, Ok(outcome(1, "", "")))
        .with(
            RemoteCommand::DatabaseServerVersion,
            Ok(outcome(127, "", "")),
        )
        .with(RemoteCommand::WebServerVersion, Ok(outcome(0, "", "")))
        .with(RemoteCommand::MailStorePresent, Ok(outcome(2, "yes\n", "")));

    let tested = probe_source(&source).unwrap();

    assert_eq!(tested.home_disk, None);
    assert_eq!(tested.system_release, None);
    assert_eq!(tested.database_server, None);
    assert_eq!(tested.web_server, None);
    assert!(
        !tested.mail_found,
        "an answer from a failed probe was believed"
    );
}

#[test]
fn text_that_does_not_parse_leaves_its_field_unknown() {
    let source = ScriptedSource::healthy()
        .with(
            RemoteCommand::HomeFilesystemUsage,
            Ok(outcome(0, "garbage", "")),
        )
        .with(
            RemoteCommand::OsRelease,
            Ok(outcome(0, "PRETTY_NAME=\"\u{1b}[31m\"\n", "")),
        );

    let tested = probe_source(&source).unwrap();

    assert_eq!(tested.home_disk, None);
    assert_eq!(tested.system_release, None);
}

#[test]
fn a_source_that_cannot_report_its_uid_is_an_invalid_answer() {
    for answer in [
        Ok(outcome(1, "", "")),
        Ok(outcome(0, "root\n", "")),
        Ok(outcome(0, "", "")),
    ] {
        let source = ScriptedSource::healthy().with(RemoteCommand::EffectiveUid, answer);

        assert!(matches!(
            probe_source(&source),
            Err(TransferOpError::InvalidRemoteOutput)
        ));
    }
}

#[test]
fn a_connection_failure_on_any_probe_is_the_error_and_stops_the_probing() {
    let source = ScriptedSource::healthy().with(
        RemoteCommand::OsRelease,
        Err((|| TransferOpError::HostKeyChanged) as fn() -> TransferOpError),
    );

    assert!(matches!(
        probe_source(&source),
        Err(TransferOpError::HostKeyChanged)
    ));
    assert_eq!(
        source.asked.lock().unwrap().len(),
        3,
        "probing went on after the failure"
    );
}
