//! Opens a pinned connection to a registered source.

use maran_distro::DistroAdapter;

use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;
use crate::transfer::open_source_connection::open_source_connection;
use crate::transfer::ssh_source_shell::SshSourceShell;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Resolves the source once, applies the address policy to the resolved address,
/// lays the credential and the pinned host key out in a fresh root-only scratch
/// directory and returns the shell that runs commands over it.
///
/// This is the one entry the connection operations that authenticate share
/// ([`crate::transfer::test_source::test_source`] and the inventory read), so the
/// order is the same for all of them: the address check comes BEFORE any file is
/// written and before any packet is sent, and the private key is written only
/// after the address was accepted. The private key is written exactly once, to a
/// `0600` file in a `0700` directory, and the returned shell removes the
/// directory when it is dropped; if any later step fails here, the scratch guard
/// created above is dropped on the way out and removes it as well. The key text
/// is written as the validated type holds it (it ends in the one line feed `ssh`
/// requires), borrowed and never copied.
///
/// # Errors
///
/// Returns [`TransferOpError::AddressRefused`] for a forbidden address,
/// [`TransferOpError::Unreachable`] for a name that does not resolve,
/// [`TransferOpError::HostAddressesUnavailable`] when the policy cannot be
/// applied, and [`TransferOpError::ScratchUnavailable`] when the scratch area or
/// a file in it cannot be made.
pub fn open_source_shell<'a>(
    host: &'a dyn TransferHost,
    distro: &'a dyn DistroAdapter,
    endpoint: &SourceEndpoint,
    credential: &SourceCredential,
) -> Result<SshSourceShell<'a>, TransferOpError> {
    let opened = open_source_connection(host, endpoint, credential)?;

    Ok(SshSourceShell::new(
        host,
        distro,
        opened.connection,
        opened.scratch,
    ))
}

#[cfg(test)]
#[path = "../tests/transfer/open_source_shell_tests.rs"]
mod tests;
