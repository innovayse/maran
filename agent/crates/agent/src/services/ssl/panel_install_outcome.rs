//! The terminal answer of `InstallPanelCertificate`, chosen from the
//! operation's outcome.

use maran_ops::ssl::SslOpError;

use crate::proto::{
    InstallCertificateRefused, InstallPanelCertificateOk, install_panel_certificate_response,
};
use crate::services::ssl::material_refusal::material_refusal;
use crate::services::ssl::ssl_status::to_agent_error;

/// Turns what `ops::ssl::install_panel_certificate` returned into the
/// response's `oneof`: the expiry, a verdict on the material naming which half
/// is wrong, or any other failure through [`to_agent_error`].
#[must_use]
pub fn panel_install_outcome(
    outcome: Result<i64, SslOpError>,
) -> install_panel_certificate_response::Result {
    let error = match outcome {
        Ok(expires_at_unix) => {
            return install_panel_certificate_response::Result::Ok(InstallPanelCertificateOk {
                expires_at_unix,
            });
        }
        Err(error) => error,
    };

    match material_refusal(error) {
        Ok(reason) => {
            install_panel_certificate_response::Result::Refused(InstallCertificateRefused {
                reason: reason as i32,
            })
        }
        Err(other) => install_panel_certificate_response::Result::Error(to_agent_error(&other)),
    }
}

#[cfg(test)]
#[path = "../../tests/services/ssl/panel_install_outcome_tests.rs"]
mod tests;
