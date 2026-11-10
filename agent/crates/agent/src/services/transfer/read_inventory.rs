//! The whole blocking body of a `ReadSourceInventory` stream.

use maran_distro::DistroAdapter;
use maran_ops::transfer::inventory::model::inventory_sink::InventorySink;
use maran_ops::transfer::inventory::model::inventory_summary::InventorySummary;
use maran_ops::transfer::inventory::read_source_inventory;
use maran_ops::transfer::{
    SourceCredential, SourceEndpoint, TransferHost, TransferOpError, open_source_shell,
};

/// Opens the pinned connection, reads the inventory over it into `sink` and lets
/// the connection go.
///
/// The shell owns the scratch directory that holds the private key, and it is
/// dropped when this function returns, on the success path and on every error
/// path alike, before the stream's terminal message is sent. The two steps are one
/// function because the order is the point: the address policy and the key file
/// are `open_source_shell`'s, the bounds and the order of the reads are
/// `read_source_inventory`'s, and neither is the handler's to interleave.
///
/// # Errors
///
/// The [`TransferOpError`] of either step: the connection failures
/// (`AddressRefused`, `HostKeyChanged`, `AuthRefused`, `Unreachable`) and the
/// faults of this host.
pub fn read_inventory(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
    endpoint: &SourceEndpoint,
    credential: &SourceCredential,
    sink: &mut dyn InventorySink,
) -> Result<InventorySummary, TransferOpError> {
    let shell = open_source_shell(host, distro, endpoint, credential)?;

    read_source_inventory(&shell, sink)
}

#[cfg(test)]
#[path = "../../tests/services/transfer/read_inventory_tests.rs"]
mod tests;
