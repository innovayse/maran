//! Where the agent records that a certificate is one of its own placeholders.

use std::path::PathBuf;

use crate::sites::SiteCertificate;

/// Name of the marker file, beside the material it describes.
const MARKER_FILE_NAME: &str = "self-signed.marker";

/// The marker path for `certificate`: a sibling of `fullchain.pem` inside the
/// agent's own store.
///
/// A FILE, and not a field of the certificate's subject, and the difference is
/// the whole point. The previous design asked openssl to print the subject and
/// looked for an `OU` component equal to a marker string — and a reviewer broke
/// it with a certificate whose organisation is literally
/// `Example, OU = maran-self-signed, more`. openssl does not escape a comma
/// inside a value, it quotes the whole value, so a splitter that does not
/// implement quoting cuts the text into a fragment that trims to an exact match.
/// The certificate really is self-signed, so the second condition held too, and
/// a customer's certificate and private key were destroyed by the marker
/// introduced to protect them.
///
/// Escaping was not the answer either: `-nameopt RFC2253` trades quotes for
/// backslashes and leaves a hand-rolled DN parser guarding the same key. The
/// whole class of bug disappears once the decision does not depend on text
/// another program formats. This directory is the agent's store — nothing but
/// the agent writes here, no customer can reach it — so the question becomes
/// `path.exists()`, with no parsing, no quoting, no encoding, and no dependence
/// on which openssl the host ships.
///
/// The generated certificate still carries `OU=maran-self-signed` in its
/// subject. That is DOCUMENTATION, so an operator running
/// `openssl x509 -subject` over this store can see what a file is. It is not the
/// decision, and it must never be turned back into one.
pub(crate) fn self_signed_marker(certificate: &SiteCertificate) -> PathBuf {
    certificate
        .certificate_path()
        .with_file_name(MARKER_FILE_NAME)
}
