//! Settling the audit slot from the answer `InstallPanelCertificate` is about to send.

use crate::audit::audit_slot::AuditSlot;
use crate::proto::{ErrorCode, install_panel_certificate_response};

/// Settles `audit` from the response's `oneof`: `ok` for the expiry, a typed
/// refusal for the error arm (its own code), and `ERROR_CODE_INVALID_INPUT` for
/// the verdict on the material, which is the caller's input being refused.
pub fn settle_panel_install_outcome(
    audit: &AuditSlot,
    outcome: &install_panel_certificate_response::Result,
) {
    match outcome {
        install_panel_certificate_response::Result::Ok(_) => audit.ok(),
        install_panel_certificate_response::Result::Refused(_) => {
            audit.refused(ErrorCode::InvalidInput);
        }
        install_panel_certificate_response::Result::Error(error) => {
            audit.settle::<()>(&Err(error.clone()));
        }
    }
}
