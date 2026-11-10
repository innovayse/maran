//! Reads the host keys a source offers.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::transfer::choose_source_address::choose_source_address;
use crate::transfer::model::scanned_host_key::ScannedHostKey;
use crate::transfer::model::source_endpoint::SourceEndpoint;
use crate::transfer::parse_scanned_host_keys::parse_scanned_host_keys;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Seconds `ssh-keyscan` waits for each connection.
const SCAN_TIMEOUT_SECONDS: &str = "10";

/// The key types asked for, in the agent's order of preference.
const KEY_TYPES: &str = "ed25519,ecdsa,rsa";

/// The most standard output kept from the scan: five short key lines.
const SCAN_OUTPUT_LIMIT_BYTES: u64 = 64 * 1024;

/// Asks the source for the host keys it offers and answers each with the
/// fingerprint to compare against the source's own console.
///
/// Nothing is authenticated and nothing but the SSH handshake is sent. The name
/// is resolved once, the address policy is applied to the resolved address, and
/// the scan connects to that literal address, so the keys read are those of the
/// address that was allowed. The result is only as trustworthy as the channel
/// the administrator compares the fingerprint over (trust on first use).
///
/// # Errors
///
/// Returns [`TransferOpError::AddressRefused`] for a forbidden resolved address,
/// [`TransferOpError::Unreachable`] when the name does not resolve, the scan
/// fails or the source offers no key, [`TransferOpError::InvalidRemoteOutput`]
/// when the source printed lines but none is a key the agent accepts, and the
/// bound and spawn errors of the program run.
pub fn scan_host_key(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
    endpoint: &SourceEndpoint,
) -> Result<Vec<ScannedHostKey>, TransferOpError> {
    let address = choose_source_address(host, endpoint)?.to_string();
    let port = endpoint.port.value().to_string();
    let outcome = host.run(
        &distro.ssh_keyscan_binary(),
        &[
            "-T",
            SCAN_TIMEOUT_SECONDS,
            "-t",
            KEY_TYPES,
            "-p",
            &port,
            "--",
            &address,
        ],
        CallClass::Query,
        SCAN_OUTPUT_LIMIT_BYTES,
    )?;

    if outcome.status != 0 {
        return Err(TransferOpError::Unreachable);
    }

    let keys = parse_scanned_host_keys(&outcome.stdout);
    if !keys.is_empty() {
        return Ok(keys);
    }

    let printed_something = outcome
        .stdout
        .lines()
        .any(|line| !line.starts_with('#') && !line.trim().is_empty());

    if printed_something {
        Err(TransferOpError::InvalidRemoteOutput)
    } else {
        Err(TransferOpError::Unreachable)
    }
}

#[cfg(test)]
#[path = "../tests/transfer/scan_host_key_tests.rs"]
mod tests;
