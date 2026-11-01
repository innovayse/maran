//! The terminal answer of `InstallCertificate`, chosen from the operation's
//! outcome.

use maran_ops::ssl::SslOpError;

use crate::proto::{InstallCertificateOk, InstallCertificateRefused, install_certificate_response};
use crate::services::ssl::material_refusal::material_refusal;
use crate::services::ssl::ssl_status::to_agent_error;

/// Turns what `ops::ssl::install_certificate` returned into the response's
/// `oneof`.
///
/// A verdict on the material itself — a half openssl cannot read, or a key
/// that is not the certificate's — is the `refused` case, naming which. The
/// caller has three different things to fix there, and an `AgentError` with
/// `ERROR_CODE_INVALID_INPUT` could only say that one of them happened.
/// Everything else is a failure of the request or of this machine, and goes
/// through [`to_agent_error`], the one mapping the area has for those.
#[must_use]
pub fn install_outcome(outcome: Result<i64, SslOpError>) -> install_certificate_response::Result {
    let error = match outcome {
        Ok(expires_at_unix) => {
            return install_certificate_response::Result::Ok(InstallCertificateOk {
                expires_at_unix,
            });
        }
        Err(error) => error,
    };

    let reason = match material_refusal(error) {
        Ok(reason) => reason,
        Err(other) => return install_certificate_response::Result::Error(to_agent_error(&other)),
    };

    install_certificate_response::Result::Refused(InstallCertificateRefused {
        reason: reason as i32,
    })
}

#[cfg(test)]
#[path = "../../tests/services/ssl/install_outcome_tests.rs"]
mod tests;
