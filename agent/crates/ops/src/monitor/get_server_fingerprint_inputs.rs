//! GetServerFingerprintInputs: the two raw values a licence's server
//! fingerprint is computed from.
//!
//! This closes the gap named in
//! `docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md`
//! §5: spec §228 requires a licence bound to a server by a fingerprint of
//! `machine-id` plus the primary network interface, and nothing in the tree
//! exposed either value. **This operation reports; it does not compute or
//! compare a fingerprint** — that is `LicenceVerifier`'s job, on the C# side,
//! and out of scope for this slice.

use maran_distro::DistroAdapter;

use crate::monitor::model::machine_identity::MachineIdentity;
use crate::monitor::model::primary_interface::PrimaryInterface;
use crate::monitor::model::server_fingerprint_inputs::ServerFingerprintInputs;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Reads this host's `machine-id` and the interface carrying its IPv4
/// default route.
///
/// No [`maran_distro::DistroAdapter`] is asked for anything here — unlike
/// `get_sftp_jail_status`, neither `/etc/machine-id` nor `/proc/net/route`
/// differs between the two supported families (see the doc comments on
/// [`MachineIdentity`] and [`PrimaryInterface`] for why), so there is no
/// platform fact for an adapter to own.
///
/// # Errors
///
/// Returns [`MonitorError::MachineIdUnavailable`] or
/// [`MonitorError::Ipv4RoutesUnavailable`] when the respective file exists
/// but could not be read — never for either value being legitimately absent,
/// which is reported inside [`ServerFingerprintInputs`] instead.
pub fn get_server_fingerprint_inputs(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<ServerFingerprintInputs, MonitorError> {
    let machine_id = MachineIdentity::from_raw(host.read_machine_id(distro.machine_id_path())?);
    let routes = host.read_ipv4_routes()?;
    let primary_interface = PrimaryInterface::from_ipv4_routes(&routes);

    Ok(ServerFingerprintInputs {
        machine_id,
        primary_interface,
    })
}

#[cfg(test)]
#[path = "../tests/monitor/get_server_fingerprint_inputs_tests.rs"]
mod tests;
