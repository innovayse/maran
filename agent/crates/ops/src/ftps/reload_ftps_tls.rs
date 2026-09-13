//! Making a running FTPS daemon pick up certificate material that was replaced
//! underneath it.

use maran_distro::adapter::DistroAdapter;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::get_ftps_status::get_ftps_status;
use crate::ftps::model::ftps_state::FtpsState;
use crate::ftps::model::ftps_unit::FtpsUnit;

/// Restarts a RUNNING FTPS daemon so it re-reads its certificate and key, then
/// checks that it came back.
///
/// # Why this is an operation and not a line in a service method
///
/// A certificate install replaces the files vsftpd read at start, and a daemon
/// holding the old material serves the old certificate until something restarts
/// it. The service layer may not do that itself — an rpc handler turns a request
/// into one `ops` call and back, with no branching and no process spawning
/// (rules/rust.md "Service anatomy") — so the restart needs a home, and this is
/// it.
///
/// `restart` and not `reload`: vsftpd has no signal that makes it re-read its
/// configuration or re-open its TLS material.
///
/// # A stopped daemon is a no-op success, not an error
///
/// A daemon that is off will read the new material at its next start by
/// construction, so there is nothing to do — and "reload TLS" must never be the
/// thing that turns a deliberately-disabled daemon on. An operator who disabled
/// FTPS and then renewed a website's certificate would otherwise find FTPS
/// running again, with no screen anywhere claiming to have started it.
///
/// # Errors
///
/// Returns [`FtpsError::ServiceRefused`] when the restart fails or the unit is
/// not active afterwards, and [`FtpsError::NotListening`] when the unit is active
/// and the control port has stopped answering — which is the shape a certificate
/// whose key does not match produces, since it parses perfectly and dies at the
/// first handshake. Returns [`FtpsError::SpawnFailed`] when the service manager
/// cannot be started at all, and [`FtpsError::ConfigUnreadable`] when the live
/// configuration exists and cannot be read while the state is observed.
pub fn reload_ftps_tls(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
) -> Result<FtpsState, FtpsError> {
    let before = get_ftps_status(host, distro, None)?;
    if !before.running {
        return Ok(before);
    }

    let restarted = host.run(distro.service_manager(), &FtpsUnit::RESTART)?;
    if restarted.status != 0 {
        return Err(FtpsError::ServiceRefused {
            unit: FtpsUnit::RESTART[1].to_owned(),
        });
    }

    let after = get_ftps_status(host, distro, None)?;
    if !after.running {
        return Err(FtpsError::ServiceRefused {
            unit: FtpsUnit::RESTART[1].to_owned(),
        });
    }
    if !after.control_port_answered {
        return Err(FtpsError::NotListening);
    }

    Ok(after)
}

#[cfg(test)]
#[path = "../tests/ftps/reload_ftps_tls_tests.rs"]
mod tests;
