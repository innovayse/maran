//! Copies one account's home from a registered source into this host.

use std::time::Duration;

use maran_distro::DistroAdapter;

use crate::transfer::inventory::parse_home_usage::parse_home_usage;
use crate::transfer::known_hosts_line::known_hosts_line;
use crate::transfer::model::opened_connection::OpenedConnection;
use crate::transfer::model::pull_phase::PullPhase;
use crate::transfer::model::pull_request::PullRequest;
use crate::transfer::model::pull_sink::PullSink;
use crate::transfer::model::pull_summary::PullSummary;
use crate::transfer::model::rsync_slice_outcome::RsyncSliceOutcome;
use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::open_source_connection::open_source_connection;
use crate::transfer::receiver_directory::ReceiverDirectory;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::rsync_argv::rsync_argv;
use crate::transfer::rsync_transport::rsync_transport;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::ssh_source_shell::SshSourceShell;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_scratch::{KEY_FILE_NAME, KNOWN_HOSTS_FILE_NAME};
use crate::transfer::transfer_work_host::TransferWorkHost;

/// How long one `rsync` run may last before it is killed and the next continues.
///
/// Strictly below the fork-as-account patience of 120 seconds, so that the
/// deadline of the run (which kills the whole `rsync`/`ssh` process group) is the
/// kill that fires, not the fork's, which reaches only its own child.
const SLICE_DEADLINE: Duration = Duration::from_secs(100);

/// How many runs a pull may take: 4 320 slices of 100 seconds is 120 hours, five
/// days, after which the pull ends with [`TransferOpError::PullIncomplete`] and
/// the panel decides whether to resume it.
const MAX_SLICES: u32 = 4_320;

/// The `rsync` exit status that means the file system is full, a quota was hit or
/// a write failed. Reported as [`TransferOpError::DiskInsufficient`].
const RSYNC_FILE_IO: i32 = 11;

/// Copies the source's home into the account's home on this host.
///
/// In order: the free-space check (the expected bytes, plus the floor), the
/// connection (resolved once, judged on the resolved address, the key in a
/// root-only directory) and ONE read-only measurement of the source home over it
/// (which also proves the pinned key before anything is written here), the
/// receiver directory (the account's, `0700`) holding a copy of the key and the
/// pinned host key, and then `rsync` in slices until it finishes.
///
/// A client that left (the sink says so) stops the pull at the next slice
/// boundary with [`TransferOpError::Cancelled`]; the partial state stays so that
/// the next call resumes. Both key directories are removed when this function
/// returns, on every path.
///
/// **Cancellation class: stop.**
///
/// # Errors
///
/// The connection failures of `open_source_connection`
/// (`AddressRefused`, `Unreachable`, `HostAddressesUnavailable`,
/// `ScratchUnavailable`), `HostKeyChanged`, `AuthRefused` or `Unreachable` from
/// the measurement, `DiskInsufficient` before the first byte or from a write that
/// ran out of room, `PullFailed` for any other failing `rsync` status,
/// `PullIncomplete` past `MAX_SLICES`, `Cancelled` when the client left, and
/// `Spawn` when the account cannot be assumed or `rsync` cannot be started.
pub fn pull_account_files(
    host: &dyn TransferHost,
    work: &dyn TransferWorkHost,
    distro: &dyn DistroAdapter,
    request: &PullRequest,
    sink: &mut dyn PullSink,
) -> Result<PullSummary, TransferOpError> {
    let needed = match request.phase {
        PullPhase::Initial => request.expected_bytes,
        PullPhase::Final => 0,
    };
    work.require_home_room(&request.account, needed)?;

    let opened = open_source_connection(host, &request.endpoint, &request.credential)?;
    let connection = opened.connection.clone();
    let source_bytes = measure_source_home(host, distro, opened, request)?;

    let receiver = ReceiverDirectory::create(work, &request.account)?;
    let key_file = receiver.file(KEY_FILE_NAME);
    let known_hosts_file = receiver.file(KNOWN_HOSTS_FILE_NAME);
    work.write_receiver_file(
        &request.account,
        &key_file,
        request.credential.private_key.expose().as_bytes(),
    )?;
    work.write_receiver_file(
        &request.account,
        &known_hosts_file,
        known_hosts_line(&request.credential.host_key).as_bytes(),
    )?;

    let account_connection = SshConnection {
        key_file,
        known_hosts_file,
        ..connection
    };
    let transport = rsync_transport(&distro.ssh_binary(), &account_connection);
    let argv = rsync_argv(request, &transport, account_connection.address);
    let arguments: Vec<&str> = argv.iter().map(String::as_str).collect();
    let rsync = distro.rsync_binary();

    let mut slices: u32 = 0;
    loop {
        if !sink.is_open() {
            return Err(TransferOpError::Cancelled);
        }

        let outcome = work.run_rsync_slice(&request.account, &rsync, &arguments, SLICE_DEADLINE)?;
        slices += 1;

        match outcome {
            RsyncSliceOutcome::Finished => {
                return Ok(PullSummary {
                    slices,
                    source_bytes,
                });
            }
            RsyncSliceOutcome::SliceEnded => {
                if slices >= MAX_SLICES {
                    return Err(TransferOpError::PullIncomplete);
                }
                sink.progress(slices);
            }
            RsyncSliceOutcome::Failed { status } => return Err(failure_of(status)),
        }
    }
}

/// Measures the source home over the opened connection and lets the connection
/// (and the root-owned key it holds) go.
///
/// A figure that cannot be read is `None`: the pull does not depend on it. A
/// connection failure is an error, because it means the pinned key did not hold
/// or the key was refused, and nothing may be copied then.
fn measure_source_home(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
    opened: OpenedConnection<'_>,
    request: &PullRequest,
) -> Result<Option<u64>, TransferOpError> {
    let shell = SshSourceShell::new(host, distro, opened.connection, opened.scratch);
    let outcome = match shell.run(&RemoteCommand::HomeDiskUsage {
        home: request.source_home.clone(),
    }) {
        Ok(outcome) => Some(outcome),
        Err(TransferOpError::OutputBound(_)) => None,
        Err(other) => return Err(other),
    };

    Ok(outcome
        .filter(|outcome| outcome.status == 0)
        .and_then(|outcome| parse_home_usage(&outcome.stdout)))
}

/// What a failing `rsync` status means.
///
/// 24 is not a failure (files vanished from a live source) and never reaches
/// here: [`RsyncSliceOutcome::of_status`] maps it to a finished run. 11 is the receiver's own write
/// failing, which on a fresh account is its disk quota or the disk itself.
fn failure_of(status: i32) -> TransferOpError {
    if status == RSYNC_FILE_IO {
        TransferOpError::DiskInsufficient
    } else {
        TransferOpError::PullFailed { status }
    }
}

#[cfg(test)]
#[path = "../tests/transfer/pull_account_files_tests.rs"]
mod tests;
