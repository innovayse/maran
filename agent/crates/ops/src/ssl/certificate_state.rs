//! What is installed for a domain, and whether it is one of ours — read, never
//! parsed.

use maran_agent_core::validation::web::domain::Domain;

use crate::sites::SiteCertificate;
use crate::ssl::model::certificate_state::CertificateState;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// Answers the two questions a caller may ask about `domain`'s certificate
/// material: is it installed, and is it one of this agent's own self-signed
/// placeholders.
///
/// # How do I know what this certificate is?
///
/// By reading a file that says so, and by parsing nothing. This function spawns
/// no process, opens no certificate, and looks at no field of one. It asks the
/// host whether three files are there — the chain, the key, and the marker
/// beside them — and reports what it was told.
///
/// That shape is the answer to the worst defect this repository has recorded.
/// The previous design asked openssl to print the certificate's subject and
/// looked for an `OU` component equal to a marker string. A reviewer broke it
/// with a certificate whose organisation is literally
/// `Example, OU = maran-self-signed, more`: openssl does not escape a comma
/// inside a value, it quotes the whole value, so a splitter without quoting cut
/// the text into a fragment that trimmed to an exact match. The certificate
/// really was self-signed, so a second condition held too — and a customer's
/// certificate and private key were destroyed by the marker introduced to
/// protect them. Escaping was not the answer either: `-nameopt RFC2253` trades
/// quotes for backslashes and leaves a hand-rolled DN parser guarding the same
/// key. The whole class of bug disappears only when the decision stops
/// depending on text another program formatted, which is why the fact is a file
/// and why nothing here reads a certificate's content.
///
/// What makes the file's mere existence sufficient is containment, and it is
/// containment by construction rather than by inspection: the marker's path is
/// derived from [`SiteCertificate::for_domain`] — which has no other
/// constructor and no settable field — inside
/// [`AgentPaths::CERTIFICATE_DIRECTORY`](maran_agent_core::agent_paths::AgentPaths),
/// a `0700` directory the agent is the only writer of. No caller-supplied
/// string reaches a path here and no path is decided by parsing its text
/// (rules/security.md §2).
///
/// # What this deliberately cannot answer
///
/// It does not verify that the material is valid, that it is in date, that the
/// key matches the certificate, or that any name in it covers the domain it is
/// filed under. `install_certificate` owns all of those and is the only thing
/// that writes here, so re-deciding them from a read would be a second opinion
/// about material this area already admitted — and a second opinion is how the
/// two halves of a decision drift apart. It also does not describe the
/// certificate's content in any way: not
/// its subject, not its issuer, not its expiry. A caller that wants an expiry
/// asks `certificate_expiry`, which spawns the tool for it.
///
/// The one thing the marker's presence CANNOT distinguish is a marker this
/// agent wrote from one root planted beside a real certificate. That is
/// accepted and named rather than defended against: only root can write in the
/// store, root can equally delete the material outright, and the alternative —
/// deciding whether a marker is "really ours" from something written inside it
/// — is the parsing this function exists to abolish. The marker's CONTENT is
/// therefore never read, and an unexpected content is still the agent's own
/// fact.
///
/// # Errors
///
/// Returns [`SslOpError::MaterialWrite`] when one of the three files exists and
/// cannot be read — the error [`SslHost::read_material`] raises. It is
/// propagated and never flattened into "absent" or "not a placeholder": an
/// unreadable marker is the state in which the agent does not know whose
/// material this is, and a function that answered `false` there would hand a
/// caller a confident answer produced by an I/O failure. That is exactly how
/// the destroyed key happened, by a different route.
pub fn certificate_state(
    host: &dyn SslHost,
    domain: &Domain,
) -> Result<CertificateState, SslOpError> {
    let certificate = SiteCertificate::for_domain(domain);
    let marker = self_signed_marker(&certificate);

    let present = host
        .read_material(certificate.certificate_path())?
        .is_some()
        && host.read_material(certificate.key_path())?.is_some();
    let is_self_signed_placeholder = present && host.read_material(&marker)?.is_some();

    Ok(CertificateState {
        certificate_path: certificate
            .certificate_path()
            .to_string_lossy()
            .into_owned(),
        private_key_path: certificate.key_path().to_string_lossy().into_owned(),
        present,
        is_self_signed_placeholder,
    })
}

#[cfg(test)]
#[path = "../tests/ssl/certificate_state_tests.rs"]
mod tests;
