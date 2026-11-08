//! The fleet listener a daemon starts when it was given the fleet flags.

use std::sync::Arc;

use super::fleet_limits::FleetLimits;
use super::fleet_listener::FleetListener;
use super::fleet_listener_error::FleetListenerError;
use super::node_identity::NodeIdentity;
use super::node_tls_identity::NodeTlsIdentity;
use super::panel_pin_file::PanelPinFile;
use crate::config::fleet_options::FleetOptions;

/// Reads the node's identity from the fleet directory and assembles the listener over it, with
/// the pin read from `panel.pem` at every handshake and request and the standard handshake
/// bounds.
///
/// Only called when both fleet flags were given; a daemon without them never reaches this and
/// has no fleet code in motion. `owner_uid` is the uid the directory must belong to, the one
/// the agent runs as.
///
/// # Errors
///
/// - [`FleetListenerError::IdentityFiles`] when the directory, `node.key` or `node.pem` is
///   missing, readable by others, or malformed.
/// - [`FleetListenerError::Pem`] when the files are text the TLS library cannot read.
pub fn build_fleet_listener(
    options: &FleetOptions,
    owner_uid: u32,
) -> Result<FleetListener, FleetListenerError> {
    let identity = NodeIdentity::read(options.directory(), owner_uid)
        .map_err(FleetListenerError::IdentityFiles)?;
    let tls_identity =
        NodeTlsIdentity::from_pem(&identity.certificate_pem, identity.private_key_pem.expose())?;
    let pin_source = Arc::new(PanelPinFile::new(options.directory().to_owned(), owner_uid));

    Ok(FleetListener::on_address(
        options.listen,
        tls_identity,
        pin_source,
        FleetLimits::standard(),
    ))
}

#[cfg(test)]
#[path = "../tests/fleet/build_fleet_listener_tests.rs"]
mod tests;
