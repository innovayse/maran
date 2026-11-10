//! Connects to a registered source with its pinned host key and probes it.

use maran_distro::DistroAdapter;

use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;
use crate::transfer::model::tested_source::TestedSource;
use crate::transfer::open_source_shell::open_source_shell;
use crate::transfer::probe_source::probe_source;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Opens a pinned connection to the source and runs the fixed probes
/// (identity, free space, system release, database server, web server, mail
/// store, panel version). Read-only on the source.
///
/// The host key the connection requires is the pinned one in `credential`, under
/// `StrictHostKeyChecking=yes`; a source that presents another is
/// [`TransferOpError::HostKeyChanged`] and is never retried here. The scratch
/// directory holding the private key is removed before this function returns,
/// on every path.
///
/// # Errors
///
/// Returns [`TransferOpError::AddressRefused`] for a forbidden resolved address,
/// [`TransferOpError::HostKeyChanged`], [`TransferOpError::AuthRefused`] or
/// [`TransferOpError::Unreachable`] for the connection,
/// [`TransferOpError::InvalidRemoteOutput`] when the source cannot even report
/// its user id, and the scratch and bound errors of the layers below.
pub fn test_source(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
    endpoint: &SourceEndpoint,
    credential: &SourceCredential,
) -> Result<TestedSource, TransferOpError> {
    let shell = open_source_shell(host, distro, endpoint, credential)?;

    probe_source(&shell)
}

#[cfg(test)]
#[path = "../tests/transfer/test_source_tests.rs"]
mod tests;
