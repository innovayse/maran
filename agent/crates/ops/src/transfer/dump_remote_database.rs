//! Dumps one database of a registered source and loads it into one here.

use std::path::PathBuf;
use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_distro::DistroAdapter;

use crate::transfer::database_loader::DatabaseLoader;
use crate::transfer::model::dump_file::DumpFile;
use crate::transfer::model::dump_request::DumpRequest;
use crate::transfer::model::dump_sink::DumpSink;
use crate::transfer::model::dump_step::DumpStep;
use crate::transfer::model::dump_summary::DumpSummary;
use crate::transfer::open_source_connection::open_source_connection;
use crate::transfer::open_source_shell::open_source_shell;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::ssh_argv::ssh_argv;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_work_host::TransferWorkHost;

/// How long the dump command may run before it is killed with its process group.
/// A dump is the one source call whose duration is the customer's data, so the
/// allowance is hours and not the minutes of an inventory command.
const DUMP_DEADLINE: Duration = Duration::from_secs(4 * 60 * 60);

/// The exit status `ssh` itself uses when it could not complete the connection.
const SSH_FAILURE_STATUS: i32 = 255;

/// A dump file that is removed whatever happens next.
struct DumpFileGuard<'a> {
    /// The host that made the file and removes it.
    work: &'a dyn TransferWorkHost,
    /// Where the file is.
    path: PathBuf,
}

impl Drop for DumpFileGuard<'_> {
    /// Removes the file. A removal that fails is logged with the file's name
    /// (random, free of customer data) and nothing of what it held.
    fn drop(&mut self) {
        if self.work.remove_dump_file(&self.path).is_err() {
            tracing::warn!(
                target: "maran_transfer",
                dump = %self.path.display(),
                "a transfer dump file could not be removed"
            );
        }
    }
}

/// Dumps `request.source_database` over the pinned channel into a root-only
/// file, then loads it into `request.target_database` through the restricted
/// loader, and removes the file on every path.
///
/// In order: the connection is proven with one read-only command (so a changed
/// host key, a refused key and an unreachable host are the typed errors they are
/// and not an anonymous failure of the dump), the file is made (refusing first
/// when the disk has no room above the floor), the dump is read into it (bounded
/// in bytes and in time), and the loader judges and loads it. A dump the loader
/// refuses is REFUSED and never loaded with more privilege.
///
/// **Cancellation class: run to completion and reclaim.** There is no safe
/// midpoint in a load, and the file and the loader's temporary user are removed
/// on every path.
///
/// # Errors
///
/// The connection errors of the preflight (`AddressRefused`, `HostKeyChanged`,
/// `AuthRefused`, `Unreachable`, `HostAddressesUnavailable`, `ScratchUnavailable`),
/// `DiskInsufficient` before the dump starts, `DumpTooLarge` when it outgrew the
/// allowance, `DumpFailed` for a failing status of the dump command,
/// `OutputBound` for its deadline, `LoadRefused` or `LoadFailed` from the loader,
/// and `Spawn` when `ssh` cannot be started.
pub fn dump_remote_database(
    host: &dyn TransferHost,
    work: &dyn TransferWorkHost,
    loader: &dyn DatabaseLoader,
    distro: &dyn DistroAdapter,
    request: &DumpRequest,
    sink: &mut dyn DumpSink,
) -> Result<DumpSummary, TransferOpError> {
    prove_connection(host, distro, request)?;

    let DumpFile { file, path } = work.create_dump_file(request.max_bytes)?;
    let _guard = DumpFileGuard { work, path };

    sink.step(DumpStep::Dumping);

    let opened = open_source_connection(host, &request.endpoint, &request.credential)?;
    let command = RemoteCommand::DumpDatabase {
        database: request.source_database.clone(),
    }
    .render();
    let argv = ssh_argv(&opened.connection, &command);
    let arguments: Vec<&str> = argv.iter().map(String::as_str).collect();
    let status = work
        .run_ssh_into(
            &distro.ssh_binary(),
            &arguments,
            &file,
            request.max_bytes,
            DUMP_DEADLINE,
        )
        .map_err(|error| match error {
            TransferOpError::OutputBound(BoundExceeded::OutputTooLarge { .. }) => {
                TransferOpError::DumpTooLarge
            }
            other => other,
        })?;
    drop(opened);

    if status == SSH_FAILURE_STATUS {
        return Err(TransferOpError::Unreachable);
    }
    if status != 0 {
        return Err(TransferOpError::DumpFailed { status });
    }

    let dump_bytes = file
        .metadata()
        .map_err(|_| TransferOpError::ScratchUnavailable)?
        .len();

    sink.step(DumpStep::Loading);
    let for_loader = file
        .try_clone()
        .map_err(|_| TransferOpError::ScratchUnavailable)?;
    loader.load(&request.target_database, for_loader)?;

    Ok(DumpSummary { dump_bytes })
}

/// Runs one read-only command over a fresh pinned connection, so the failure of
/// the connection itself is classified before a dump is attempted.
///
/// The dump's `ssh` exits 255 for every way the connection can fail and its
/// standard error is not read, so the typed reasons (a changed host key, a
/// refused key, an unreachable host) come from here and only from here.
fn prove_connection(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
    request: &DumpRequest,
) -> Result<(), TransferOpError> {
    let shell = open_source_shell(host, distro, &request.endpoint, &request.credential)?;

    match shell.run(&RemoteCommand::EffectiveUid) {
        Ok(_) | Err(TransferOpError::OutputBound(_)) => Ok(()),
        Err(other) => Err(other),
    }
}

#[cfg(test)]
#[path = "../tests/transfer/dump_remote_database_tests.rs"]
mod tests;
