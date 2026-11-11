//! Keeping the Apache package from starting itself while it installs.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::apps::nginx_port80::{restore_nginx_port80, suspend_nginx_port80};
use crate::apps::prepare_apache::failed;
use crate::apps::{AppsHost, AppsOpError};

/// The service-manager subcommand that makes a unit unstartable.
const MASK: &str = "mask";

/// The service-manager subcommand that undoes [`MASK`].
const UNMASK: &str = "unmask";

/// The service-manager subcommand that asks how a unit is enabled.
const IS_ENABLED: &str = "is-enabled";

/// What `is-enabled` prints (on standard output) for a masked unit; `masked-runtime` is the
/// mask that does not survive a reboot.
const MASKED: &str = "masked";

/// The hold on Apache's start while its package installs (issue #529): nginx's port-80 block
/// parked and the service masked, for exactly as long as this value lives.
///
/// The hold never outlives the operation: [`Self::release`] undoes it and reports a failure, and
/// a hold that is dropped any other way (an early return, a panic) undoes it best-effort, so no
/// path out of the install leaves the unit masked or nginx off port 80. What a dead process
/// leaves behind (a kill between the mask and the release) is healed by the next install
/// ([`unmask_if_masked`]).
pub(crate) struct ApacheHold<'a, H: AppsHost> {
    host: &'a H,
    distro: &'a dyn DistroAdapter,
    /// Whether anything was held and is not yet given back.
    held: bool,
}

/// Makes the Apache install safe on a family whose package starts the service in its own
/// maintainer script (issue #529).
///
/// That start binds port 80, which nginx still holds, and a failed bind fails the script and
/// leaves the package half configured. So, BEFORE the package is installed, nginx's port-80 block
/// is parked (the same step `prepare_apache` repeats, idempotently) and the service is masked: a
/// masked unit cannot be started by the script, whatever it asks. Nothing is held on a family
/// whose package leaves the service stopped.
///
/// A failure of either step puts back what was done and is the error returned, so a refused
/// attempt leaves the host as it was.
///
/// # Errors
///
/// Returns [`AppsOpError::Php`] with the write protocol's refusal for nginx,
/// [`AppsOpError::ServiceManager`] when the unit could not be masked and
/// [`AppsOpError::Package`] with a bound when a program outlived its deadline.
pub(crate) fn hold_apache_off<'a, H: AppsHost>(
    host: &'a H,
    distro: &'a dyn DistroAdapter,
) -> Result<ApacheHold<'a, H>, AppsOpError> {
    if !distro.apache_install_starts_service() {
        return Ok(ApacheHold {
            host,
            distro,
            held: false,
        });
    }

    suspend_nginx_port80(host, distro)?;
    if let Err(error) = run_on_service(host, distro, MASK) {
        let _ = restore_nginx_port80(host, distro);
        return Err(error);
    }
    Ok(ApacheHold {
        host,
        distro,
        held: true,
    })
}

impl<H: AppsHost> ApacheHold<'_, H> {
    /// Gives everything back: the unit is unmasked and nginx gets its port-80 block again. Both
    /// are tried whatever the first one says; `prepare_apache` parks the block again right
    /// before it starts Apache, so the port is off nginx only for the package step and for the
    /// start, never for the steps in between.
    ///
    /// # Errors
    ///
    /// Returns [`AppsOpError::ServiceManager`] when the unit could not be unmasked,
    /// [`AppsOpError::Php`] with the write protocol's refusal for nginx, and
    /// [`AppsOpError::Package`] with a bound when a program outlived its deadline.
    pub(crate) fn release(mut self) -> Result<(), AppsOpError> {
        self.give_back()
    }

    fn give_back(&mut self) -> Result<(), AppsOpError> {
        if !self.held {
            return Ok(());
        }
        self.held = false;
        let unmasked = run_on_service(self.host, self.distro, UNMASK);
        let restored = restore_nginx_port80(self.host, self.distro);
        unmasked.and(restored)
    }
}

impl<H: AppsHost> Drop for ApacheHold<'_, H> {
    fn drop(&mut self) {
        // Best effort: a hold abandoned by an early return or a panic has no caller left to tell.
        let _ = self.give_back();
    }
}

/// Unmasks the Apache unit when it is masked, and does nothing otherwise.
///
/// The recovery half of the hold: a process that died between the mask and the release (the
/// package run is long) leaves the unit masked, the package present and every retry failing on
/// `systemctl enable --now` of a masked unit. So every Apache install starts here, and a mask
/// left by an earlier run is gone before anything depends on the unit. Only a family that
/// masks (`apache_install_starts_service`) is asked.
///
/// # Errors
///
/// Returns [`AppsOpError::ServiceManager`] when the unit is masked and could not be unmasked and
/// [`AppsOpError::Package`] with a bound when the service manager outlived its deadline.
pub(crate) fn unmask_if_masked<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
) -> Result<(), AppsOpError> {
    if !distro.apache_install_starts_service() {
        return Ok(());
    }

    let state = host
        .run(
            &distro.service_manager(),
            &[IS_ENABLED, END_OF_OPTIONS, distro.apache_service()],
            CallClass::Query,
        )
        .map_err(failed)?;
    // `is-enabled` exits non-zero for a masked unit and prints its state on standard output.
    if state.stdout.trim().starts_with(MASKED) {
        return run_on_service(host, distro, UNMASK);
    }
    Ok(())
}

/// Runs `systemctl <subcommand> -- <apache unit>`.
fn run_on_service<H: AppsHost>(
    host: &H,
    distro: &dyn DistroAdapter,
    subcommand: &str,
) -> Result<(), AppsOpError> {
    let outcome = host
        .run(
            &distro.service_manager(),
            &[subcommand, END_OF_OPTIONS, distro.apache_service()],
            CallClass::Reload,
        )
        .map_err(failed)?;
    if outcome.status != 0 {
        return Err(AppsOpError::ServiceManager {
            stderr: outcome.stderr,
        });
    }
    Ok(())
}
