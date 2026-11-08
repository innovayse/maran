//! Reading the whole mail status from the host.

use maran_distro::DistroAdapter;

use crate::mail::get_mail_status::get_mail_status;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::observed_mail::ObservedMail;
use crate::mail::observe_mail::observe_mail;

/// Reads the mail stack's state and the readings beside it, for the status rpc.
///
/// `version_id` is the os-release `VERSION_ID` the daemon read at start, which
/// picks the key-value server whose unit is asked about.
///
/// A host where mail was never enabled answers a state with every daemon stopped
/// and every port unbound; that is a state, not an error.
///
/// # Errors
///
/// Returns [`MailError::UnsupportedRelease`] for a release the adapter has no
/// answer for, and the errors of [`get_mail_status`] otherwise.
pub fn read_mail_status(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    version_id: &str,
) -> Result<ObservedMail, MailError> {
    let layout = MailLayout::resolve(distro, version_id)?;
    let state = get_mail_status(host, distro, &layout)?;
    Ok(observe_mail(host, distro, state))
}

#[cfg(test)]
#[path = "../tests/mail/read_mail_status_tests.rs"]
mod tests;
