//! The request describing a placeholder certificate, shared by every operation
//! that generates one.

use maran_agent_core::validation::web::domain::Domain;

use crate::ssl::model::self_signed_request::SelfSignedRequest;

/// How long the placeholder is valid for, in days.
///
/// Ten years, because nothing trusts it anyway: a browser refuses a self-signed
/// certificate on day one exactly as it refuses it on day 3651, so a short
/// validity would buy no security and would instead produce an expiry the panel
/// schedules a renewal for — a renewal of a certificate the panel is trying to
/// replace with a real one.
const VALIDITY_DAYS: u32 = 3650;

/// The key openssl generates, in its `-newkey` notation.
const KEY_SPECIFICATION: &str = "rsa:2048";

/// The organisational unit every placeholder this agent writes carries.
///
/// The marker that makes "may I replace this?" a question with a sufficient
/// answer rather than a necessary one. `subject == issuer` only says
/// "self-signed", which is equally true of the certificate a customer generated
/// for their staging box by hand — and replacing THAT on a retry destroys their
/// certificate and their key with no warning and no recovery. A certificate
/// without this marker is left alone even when it is self-signed, because
/// refusing to overwrite something unknown is the safe default and the panel has
/// `InstallCertificate` for when it means to replace.
const SELF_SIGNED_MARKER: &str = "maran-self-signed";

/// The attribute the marker is carried in, as openssl names it.
const ORGANISATIONAL_UNIT: &str = "OU";

/// The request describing the placeholder for `domain` and its `aliases`.
///
/// Every part of it is composed from validated `Domain`s: a `Domain` cannot
/// contain a comma, a slash, an equals sign or a newline, which is what makes
/// it safe to put into the `-subj` and `-addext` values openssl parses itself.
/// The aliases are included, because a certificate that does not name a host is
/// not accepted for it — a placeholder that covers only the primary domain
/// leaves every alias failing differently from the primary, which is the sort
/// of difference that is found in production. The panel's own placeholder has
/// no aliases and passes an empty slice.
///
/// Shared by `generate_self_signed` (a site's placeholder) and
/// `restore_panel_self_signed` (the panel's), so the validity, the key and the
/// subject marker are one decision and not two copies that drift.
pub(crate) fn self_signed_request_for(domain: &Domain, aliases: &[Domain]) -> SelfSignedRequest {
    let mut names = vec![format!("DNS:{}", domain.as_str())];
    names.extend(
        aliases
            .iter()
            .map(|alias| format!("DNS:{}", alias.as_str())),
    );

    SelfSignedRequest {
        // The marker is part of the subject, so it survives everywhere the
        // certificate goes — a file beside it would not survive a restore, a
        // copy, or an operator moving the store.
        subject: format!(
            "/CN={}/{ORGANISATIONAL_UNIT}={SELF_SIGNED_MARKER}",
            domain.as_str()
        ),
        subject_alternative_name: format!("subjectAltName={}", names.join(",")),
        days: VALIDITY_DAYS,
        key_specification: KEY_SPECIFICATION.to_owned(),
    }
}

#[cfg(test)]
#[path = "../tests/ssl/self_signed_request_for_tests.rs"]
mod tests;
