//! Reading the inventory of a registered source.

use super::inventory_run::InventoryRun;
use super::model::inventory_sink::InventorySink;
use super::model::inventory_step::InventoryStep;
use super::model::inventory_summary::InventorySummary;
use super::read_accounts::read_accounts;
use super::read_cron::read_cron;
use super::read_databases::read_databases;
use super::read_disk::read_disk;
use super::read_sites::read_sites;
use crate::transfer::{SourceShell, TransferOpError};

/// Reads what a source serves — accounts, sites, databases, crontabs and sizes —
/// with the closed set of read-only commands, and streams it to `sink`.
///
/// The order is fixed: accounts, sites, databases, cron, disk. `sink` is told of
/// each step as it starts, hears every warning as it arises, and receives each
/// account once, complete, after the last step. Nothing on the source is written
/// and nothing from it is trusted: every name is validated by the ordinary
/// validators and a name that fails is a warning and a skipped entry, never a
/// repaired one; every output and the number of accounts, files and warnings are
/// bounded, and a bound reached sends `Truncated` and goes on with what was read.
///
/// An unreadable part (a crontab, the databases, a vhost file, a home) is a
/// warning and never ends the read. What ends it is the account list failing, or
/// the connection itself: a host key that changed, a refused key, an unreachable
/// source. A sink that stops listening ends it quietly between steps.
///
/// **Cancellation: stop.** There is nothing to reclaim; the caller owns the
/// private key's scratch and removes it when this returns.
///
/// # Errors
///
/// - [`TransferOpError::InvalidRemoteOutput`] when the account list could not be
///   read.
/// - [`TransferOpError::HostKeyChanged`], [`TransferOpError::AuthRefused`] or
///   [`TransferOpError::Unreachable`] when the connection failed.
/// - [`TransferOpError::Spawn`] when the client could not be run.
pub fn read_source_inventory(
    shell: &dyn SourceShell,
    sink: &mut dyn InventorySink,
) -> Result<InventorySummary, TransferOpError> {
    let mut run = InventoryRun::new(shell, sink);

    if !run.begin(InventoryStep::Accounts) {
        return Ok(run.summary());
    }
    let mut drafts = read_accounts(&mut run)?;

    if !run.begin(InventoryStep::Sites) {
        return Ok(run.summary());
    }
    read_sites(&mut run, &mut drafts)?;

    if !run.begin(InventoryStep::Databases) {
        return Ok(run.summary());
    }
    read_databases(&mut run, &mut drafts)?;

    if !run.begin(InventoryStep::Cron) {
        return Ok(run.summary());
    }
    read_cron(&mut run, &mut drafts)?;

    if !run.begin(InventoryStep::Disk) {
        return Ok(run.summary());
    }
    read_disk(&mut run, &mut drafts)?;

    for draft in drafts {
        run.send_account(draft.into_record());
    }
    Ok(run.summary())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_source_inventory_tests.rs"]
mod tests;
