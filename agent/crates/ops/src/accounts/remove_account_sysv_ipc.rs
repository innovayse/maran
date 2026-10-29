//! Removing the SysV IPC objects an account's uid owns before the uid is freed.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::model::sysv_ipc_object::SysvIpcObject;
use crate::accounts::parse_sysv_ipc_table::parse_sysv_ipc_table;
use crate::accounts::{AccountError, SystemHost};

/// Every kind of object, in the order they are removed.
const KINDS: [SysvIpcKind; 3] = [SysvIpcKind::Shm, SysvIpcKind::Msg, SysvIpcKind::Sem];

/// The `ipcrm` option that selects `kind` by identifier.
///
/// The long spellings are `--shmem-id`, `--queue-id` and `--semaphore-id`.
/// To be confirmed on util-linux 2.37 (AlmaLinux 9) and 2.39 (Ubuntu 24.04)
/// before release; an unknown option makes `ipcrm` exit non-zero, which this
/// operation reports as a refusal and never as success.
const fn selector(kind: SysvIpcKind) -> &'static str {
    match kind {
        SysvIpcKind::Shm => "--shmem-id",
        SysvIpcKind::Msg => "--queue-id",
        SysvIpcKind::Sem => "--semaphore-id",
    }
}

/// The objects of `kind` that belong to `uid`: owned by it or created by it.
fn owned_by(
    host: &dyn SystemHost,
    kind: SysvIpcKind,
    uid: u32,
) -> Result<Vec<SysvIpcObject>, AccountError> {
    let table = host.read_sysv_ipc_table(kind)?;
    Ok(parse_sysv_ipc_table(kind, &table)?
        .into_iter()
        .filter(|object| object.uid == uid || object.cuid == uid)
        .collect())
}

/// Removes every SysV shared-memory segment, message queue and semaphore set
/// that `account`'s uid owns or created, and verifies none is left.
///
/// Such an object outlives every process and is keyed by a bare uid, so the
/// next account given that uid would inherit it.
///
/// # How
///
/// For each kind the table under `/proc/sysvipc` is read and parsed; each
/// object whose `uid` or `cuid` is the account's uid is removed with
/// `ipcrm <selector> <id>` (argv only, no shell, the id rendered with
/// `Display`); the tables are then read again and anything still matching ends
/// the operation. The uid comes only from the [`ManagedAccount`], which refuses
/// uid 0 and system ids, so a system user's objects cannot be named here. The
/// selector spelling is to be confirmed on util-linux 2.37 and 2.39.
///
/// Must be called with the account's lock held. That lock is process-local.
///
/// # Errors
///
/// - [`AccountError::HomeInspection`] when a table exists but cannot be read.
/// - [`AccountError::UnreadableOutput`] when a table has a shape this agent
///   cannot parse.
/// - [`AccountError::CommandUnavailable`] when `ipcrm` could not be started.
/// - [`AccountError::Bound`] when `ipcrm` outran its deadline.
/// - [`AccountError::CommandFailed`] when `ipcrm` exited non-zero.
/// - [`AccountError::IpcRemain`] when the re-read still lists an object of
///   the uid. Nothing has been deleted at that point.
pub(crate) fn remove_account_sysv_ipc(
    host: &dyn SystemHost,
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    let ipcrm = distro.ipcrm_binary();
    let uid = account.uid();

    for kind in KINDS {
        for object in owned_by(host, kind, uid)? {
            let id = object.id.to_string();
            let outcome = host.run(&ipcrm, &[selector(kind), id.as_str()], CallClass::Mutation)?;
            if outcome.status != 0 {
                return Err(AccountError::command_failed(ipcrm.as_str(), &outcome));
            }
        }
    }

    for kind in KINDS {
        if !owned_by(host, kind, uid)?.is_empty() {
            tracing::warn!(
                username = account.name().as_str(),
                uid = uid,
                table = kind.table_name(),
                "SysV IPC objects of the account survived removal; the deletion was stopped before userdel"
            );
            return Err(AccountError::IpcRemain {
                username: account.name().as_str().to_owned(),
            });
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "../tests/accounts/remove_account_sysv_ipc_tests.rs"]
mod tests;
