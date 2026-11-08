//! Turning mail off, and keeping it off across a reboot and a package upgrade.

use maran_distro::DistroAdapter;

use crate::mail::get_mail_status::get_mail_status;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_state::MailState;
use crate::mail::stop_mail_daemons::stop_mail_daemons;

/// Stops the mail stack, takes it out of the boot sequence and masks its units.
///
/// Idempotent: a stack that is already off converges, because `disable --now` of a
/// stopped, disabled unit and `mask` of a masked one both exit zero and do no work.
///
/// **Configuration, maps, Maildirs and keys stay exactly where they are.** Only the
/// daemons stop. Removing a customer's mail because an administrator paused the
/// service would turn a pause into a loss, and keeping the files means a later
/// enable converges without rewriting them.
///
/// The state is observed afterwards rather than assumed: this function's success
/// is not evidence that the ports closed, and a state that said so because the
/// commands exited zero would be the panel agreeing with itself.
///
/// # Cancellation class
///
/// Run to completion. Four stops in sequence with the first done and the last not
/// is a stack half-listening; the call is idempotent, so the remedy for a lost
/// stream is to ask again.
///
/// # Errors
///
/// Returns [`MailError::ServiceRefused`] naming the first unit the service manager
/// refused (the others were still attempted), [`MailError::SpawnFailed`] when it
/// cannot be started, [`MailError::Bound`] when it outruns its deadline, and
/// [`MailError::SocketsUnreadable`] when the resulting state cannot be observed.
pub fn disable_mail(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    version_id: &str,
) -> Result<MailState, MailError> {
    let layout = MailLayout::resolve(distro, version_id)?;
    stop_mail_daemons(host, distro, &layout)?;
    get_mail_status(host, distro, &layout)
}

#[cfg(test)]
#[path = "../tests/mail/disable_mail_tests.rs"]
mod tests;
