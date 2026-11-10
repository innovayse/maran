//! Runs the fixed probes on an open source connection.

use crate::transfer::model::source_kind::SourceKind;
use crate::transfer::model::tested_source::TestedSource;
use crate::transfer::probes::parse_database_server::parse_database_server;
use crate::transfer::probes::parse_effective_uid::parse_effective_uid;
use crate::transfer::probes::parse_home_filesystem::parse_home_filesystem;
use crate::transfer::probes::parse_mail_found::parse_mail_found;
use crate::transfer::probes::parse_os_release::parse_os_release;
use crate::transfer::probes::parse_panel_version::parse_panel_version;
use crate::transfer::probes::parse_web_server::parse_web_server;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::transfer_op_error::TransferOpError;

/// Runs one probe and returns what it printed, or `None` when it did not exit
/// with status 0 (the tool is absent, the file unreadable): an unanswered probe
/// is an unknown, not a failure.
///
/// # Errors
///
/// Returns the shell's error when the connection itself failed.
fn answer(
    shell: &dyn SourceShell,
    command: &RemoteCommand,
) -> Result<Option<String>, TransferOpError> {
    let outcome = shell.run(command)?;

    Ok((outcome.status == 0).then_some(outcome.stdout))
}

/// Asks the source the fixed probes, in order, and returns what it answered.
///
/// The first probe, the effective uid, is the one that must answer: a shell
/// that cannot run `id -u` is not a shell this agent can read an inventory
/// from, so a missing or malformed answer is
/// [`TransferOpError::InvalidRemoteOutput`]. Every other probe that does not
/// answer leaves its field unknown. A source is a Maran panel exactly when its
/// release state file names a version.
///
/// # Errors
///
/// Returns the connection's own errors ([`TransferOpError::HostKeyChanged`],
/// [`TransferOpError::AuthRefused`], [`TransferOpError::Unreachable`],
/// [`TransferOpError::OutputBound`], [`TransferOpError::Spawn`]) from any probe,
/// and [`TransferOpError::InvalidRemoteOutput`] when the effective uid cannot be
/// read.
pub fn probe_source(shell: &dyn SourceShell) -> Result<TestedSource, TransferOpError> {
    let uid = answer(shell, &RemoteCommand::EffectiveUid)?
        .and_then(|text| parse_effective_uid(&text))
        .ok_or(TransferOpError::InvalidRemoteOutput)?;
    let home_disk = answer(shell, &RemoteCommand::HomeFilesystemUsage)?
        .and_then(|text| parse_home_filesystem(&text));
    let system_release =
        answer(shell, &RemoteCommand::OsRelease)?.and_then(|text| parse_os_release(&text));
    let database_server = answer(shell, &RemoteCommand::DatabaseServerVersion)?
        .and_then(|text| parse_database_server(&text));
    let web_server =
        answer(shell, &RemoteCommand::WebServerVersion)?.and_then(|text| parse_web_server(&text));
    let mail_found = answer(shell, &RemoteCommand::MailStorePresent)?
        .is_some_and(|text| parse_mail_found(&text));
    let maran_version = answer(shell, &RemoteCommand::MaranPanelVersion)?
        .and_then(|text| parse_panel_version(&text));

    Ok(TestedSource {
        is_root: uid == 0,
        home_disk,
        system_release,
        kind: if maran_version.is_some() {
            SourceKind::Maran
        } else {
            SourceKind::GenericLinux
        },
        maran_version,
        database_server,
        web_server,
        mail_found,
    })
}

#[cfg(test)]
#[path = "../tests/transfer/probe_source_tests.rs"]
mod tests;
