//! Lays a registered source's credential out for one connection.

use crate::transfer::choose_source_address::choose_source_address;
use crate::transfer::model::opened_connection::OpenedConnection;
use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;
use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_scratch::{KEY_FILE_NAME, KNOWN_HOSTS_FILE_NAME, TransferScratch};
use crate::transfer::write_known_hosts::write_known_hosts;

/// Resolves the source once, applies the address policy to the resolved address,
/// and lays the credential and the pinned host key out in a fresh root-only
/// scratch directory.
///
/// The one place a connection to a source is prepared: the shell the inventory
/// reads over ([`crate::transfer::open_source_shell::open_source_shell`]) and the
/// database dump both start here, so the order is the same for all of them. The
/// address check comes BEFORE any file is written and before any packet is sent,
/// and the private key is written only after the address was accepted, exactly
/// once, to a `0600` file in a `0700` directory. If a later step fails here, the
/// scratch guard created above is dropped on the way out and removes it.
///
/// # Errors
///
/// Returns [`TransferOpError::AddressRefused`] for a forbidden address,
/// [`TransferOpError::Unreachable`] for a name that does not resolve,
/// [`TransferOpError::HostAddressesUnavailable`] when the policy cannot be
/// applied, and [`TransferOpError::ScratchUnavailable`] when the scratch area or
/// a file in it cannot be made.
pub fn open_source_connection<'a>(
    host: &'a dyn TransferHost,
    endpoint: &SourceEndpoint,
    credential: &SourceCredential,
) -> Result<OpenedConnection<'a>, TransferOpError> {
    let address = choose_source_address(host, endpoint)?;
    let scratch = TransferScratch::create(host)?;
    let key_file = scratch.file(KEY_FILE_NAME);
    let known_hosts_file = scratch.file(KNOWN_HOSTS_FILE_NAME);

    host.write_private_file(&key_file, credential.private_key.expose().as_bytes())?;
    write_known_hosts(host, &known_hosts_file, &credential.host_key)?;

    let connection = SshConnection {
        address,
        port: endpoint.port,
        user: endpoint.user.clone(),
        key_file,
        known_hosts_file,
        host_key_algorithm: credential.host_key.algorithm().to_owned(),
    };

    Ok(OpenedConnection {
        connection,
        scratch,
    })
}
