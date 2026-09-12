//! Turning FTPS off, and keeping it off across a reboot.

use maran_distro::adapter::DistroAdapter;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::get_ftps_status::get_ftps_status;
use crate::ftps::model::ftps_state::FtpsState;
use crate::ftps::model::ftps_unit::FtpsUnit;

/// Stops the FTPS daemon and takes it out of the boot sequence.
///
/// Idempotent: a daemon that is already off is a success that touches nothing —
/// `systemctl disable --now` on a stopped, disabled unit exits zero and does no
/// work, which is what makes a repeated disable safe after a lost response.
///
/// Both halves in one command, deliberately. Stopping without disabling leaves a
/// unit that comes back at the next reboot, which is the operator's decision
/// silently undone by a power cut.
///
/// The configuration file is left exactly where it is. It is inert — vsftpd reads
/// it only when the unit starts — so removing it would buy no safety, and keeping
/// it means a later enable of the same configuration converges without rewriting
/// anything and without bouncing a daemon.
///
/// # Errors
///
/// Returns [`FtpsError::ServiceRefused`] when the service manager refuses to stop
/// or disable the unit, and [`FtpsError::SpawnFailed`] when it cannot be started
/// at all. Returns [`FtpsError::ConfigUnreadable`] when the live configuration
/// exists and cannot be read while the resulting state is observed.
pub fn disable_ftps(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
) -> Result<FtpsState, FtpsError> {
    let outcome = host.run(distro.service_manager(), &FtpsUnit::DISABLE_NOW)?;
    if outcome.status != 0 {
        return Err(FtpsError::ServiceRefused {
            unit: FtpsUnit::DISABLE_NOW[2].to_owned(),
        });
    }

    // Observed afterwards rather than assumed: this function's own success is not
    // evidence that the daemon stopped, and a state that said so because the
    // command exited zero would be the panel agreeing with itself.
    get_ftps_status(host, distro, None)
}

#[cfg(test)]
#[path = "../tests/ftps/disable_ftps_tests.rs"]
mod tests;
