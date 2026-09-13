//! GenerateSelfSigned: the placeholder a site serves until a real certificate
//! arrives.

use maran_distro::DistroAdapter;

use crate::sites::SiteCertificate;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::ssl::install_certificate::install_certificate;
use crate::ssl::model::self_signed_request::SelfSignedRequest;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

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

/// Prints the certificate's subject and issuer, in that order.
const SUBJECT_AND_ISSUER: [&str; 4] = ["x509", "-noout", "-subject", "-issuer"];

/// Generates a self-signed certificate for `site` and installs it, returning
/// its expiry as Unix seconds.
///
/// The point of the operation is a site that can answer on 443 at all before
/// its real certificate exists: nginx will not start a TLS server block whose
/// `ssl_certificate` is missing, so without a placeholder the site cannot be
/// configured for HTTPS until the moment the certificate arrives, and the
/// switch-on becomes a config change rather than a file swap.
///
/// Idempotent as the contract requires (`ssl.proto`), with the one rule that
/// matters here: regenerating REPLACES a previous self-signed placeholder, and
/// never overwrites a real certificate — a caller that went to a certificate
/// authority gets [`SslOpError::AlreadyExists`] instead. Overwriting one would
/// replace a trusted certificate with one every browser refuses, which is an
/// outage produced by a retry.
///
/// Replaceable is decided by a MARKER FILE this agent writes beside the material
/// it generated, plus the certificate being self-signed as a second condition.
/// Self-signed alone is necessary and nowhere near sufficient — the certificate
/// a customer generated for their staging box satisfies it too, and overwriting
/// that destroys their key silently. Anything without the marker is
/// [`SslOpError::AlreadyExists`], including a self-signed certificate this agent
/// did not write, and including one whose subject merely CONTAINS the marker
/// text: the decision is a file's existence, not text a parser has to interpret.
///
/// # Errors
///
/// Returns [`SslOpError::AlreadyExists`] when a certificate that is not
/// self-signed is installed for the domain, [`SslOpError::MalformedCertificate`]
/// when the installed certificate or openssl's request cannot be read, and
/// everything [`install_certificate`] returns, since the generated material
/// takes exactly the same path into the store as a real one.
pub fn generate_self_signed(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    site: &CreateSiteInput,
) -> Result<i64, SslOpError> {
    let certificate = SiteCertificate::for_domain(&site.domain);

    if let Some(installed) = host.read_material(certificate.certificate_path())?
        && !is_replaceable_placeholder(host, distro, &certificate, &installed)?
    {
        return Err(SslOpError::AlreadyExists {
            domain: site.domain.as_str().to_owned(),
        });
    }

    let material = host.generate_material(distro.openssl_binary(), &request_for(site))?;
    let expiry = install_certificate(host, distro, site, &material)?;

    // AFTER the material, and after `install_certificate` has cleared any marker
    // that was there: the file says "the bytes currently in this directory are a
    // placeholder", so it must never be written before the bytes it describes,
    // and must never survive them.
    host.write_self_signed_marker(&self_signed_marker(&certificate))?;

    Ok(expiry)
}

/// The request describing the placeholder for `site`.
///
/// Every part of it is composed from validated `Domain`s: a `Domain` cannot
/// contain a comma, a slash, an equals sign or a newline, which is what makes
/// it safe to put into the `-subj` and `-addext` values openssl parses itself.
/// The aliases are included, because a certificate that does not name a host is
/// not accepted for it — a placeholder that covers only the primary domain
/// leaves every alias failing differently from the primary, which is the sort
/// of difference that is found in production.
fn request_for(site: &CreateSiteInput) -> SelfSignedRequest {
    let mut names = vec![format!("DNS:{}", site.domain.as_str())];
    names.extend(
        site.aliases
            .iter()
            .map(|alias| format!("DNS:{}", alias.as_str())),
    );

    SelfSignedRequest {
        // The marker is part of the subject, so it survives everywhere the
        // certificate goes — a file beside it would not survive a restore, a
        // copy, or an operator moving the store.
        subject: format!(
            "/CN={}/{ORGANISATIONAL_UNIT}={SELF_SIGNED_MARKER}",
            site.domain.as_str()
        ),
        subject_alternative_name: format!("subjectAltName={}", names.join(",")),
        days: VALIDITY_DAYS,
        key_specification: KEY_SPECIFICATION.to_owned(),
    }
}

/// Whether the installed certificate is a placeholder THIS agent wrote and may
/// replace.
///
/// Two conditions, and the FIRST is the decision: a marker file must sit beside
/// the material in the agent's own store. The second — the certificate is
/// self-signed, its issuer being its own subject — is belt and braces, kept
/// because it costs one process the operation was going to spawn anyway.
///
/// The marker used to be an `OU` component of the subject, read back out of
/// openssl's printed text. A reviewer broke that with a certificate whose
/// organisation is `Example, OU = maran-self-signed, more`: openssl quotes a
/// value containing a comma rather than escaping it, so a splitter without
/// quoting produces a fragment that trims to an exact match, and the customer's
/// certificate and key were destroyed by the very check meant to save them.
/// Nothing here parses a subject any more. `OU=maran-self-signed` is still put
/// in the generated certificate, but as documentation for an operator reading
/// the store — the FILE is authoritative, and turning the subject back into the
/// decision would restore a bug a parser cannot be trusted to avoid.
///
/// Only the public half is involved, so nothing here can leak a key.
///
/// # Errors
///
/// Returns [`SslOpError::MalformedCertificate`] when openssl will not read the
/// installed certificate. A certificate already on disk that cannot be read is
/// NOT treated as a placeholder: that reading would overwrite a real
/// certificate whose file happens to be damaged.
fn is_replaceable_placeholder(
    host: &dyn SslHost,
    distro: &dyn DistroAdapter,
    certificate: &SiteCertificate,
    certificate_pem: &str,
) -> Result<bool, SslOpError> {
    if host
        .read_material(&self_signed_marker(certificate))?
        .is_none()
    {
        // No marker, so this agent did not put it here — or a real certificate
        // has since replaced what it did. Either way it is not ours to destroy.
        return Ok(false);
    }

    let outcome = host.run_with_certificate(
        distro.openssl_binary(),
        &SUBJECT_AND_ISSUER,
        certificate_pem,
    )?;
    if outcome.status != 0 {
        return Err(SslOpError::MalformedCertificate {
            reason: outcome.stderr,
        });
    }

    let field = |name: &str| {
        outcome
            .stdout
            .lines()
            .find_map(|line| line.trim().strip_prefix(name).map(str::trim))
            .map(str::to_owned)
    };

    match (field("subject="), field("issuer=")) {
        // Compared whole, as two opaque strings. This is not parsing: it asks
        // whether openssl printed the same text twice, which no quoting rule can
        // turn into a false positive.
        (Some(subject), Some(issuer)) => Ok(subject == issuer),
        // A certificate openssl read but did not describe is not something to
        // guess about, and the safe guess is "not a placeholder".
        _ => Ok(false),
    }
}

#[cfg(test)]
#[path = "../tests/ssl/generate_self_signed_tests.rs"]
mod tests;
