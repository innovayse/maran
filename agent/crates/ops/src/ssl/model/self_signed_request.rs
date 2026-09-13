//! What a self-signed certificate is generated to say.

/// The parameters of the self-signed placeholder a site serves until a real
/// certificate arrives.
///
/// Built by [`super::super::generate_self_signed::generate_self_signed`] from a
/// validated `Domain` and handed to the host, so the operation decides what the
/// certificate says and the host only spawns openssl with it. Every field is
/// derived from validated input — a `Domain` cannot contain a comma, a newline
/// or a `/` — which is what makes it safe to compose into the `-subj` and
/// `-addext` arguments openssl parses itself.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SelfSignedRequest {
    /// The subject in openssl's own notation, e.g. `/CN=example.com`.
    pub subject: String,
    /// The `subjectAltName` extension, e.g.
    /// `subjectAltName=DNS:example.com,DNS:www.example.com`.
    ///
    /// Present rather than left to the subject alone because every current
    /// browser ignores the common name entirely: a certificate without this
    /// extension is refused even as a placeholder, which would make the
    /// placeholder useless for the one thing it exists for — letting a site
    /// answer on 443 at all before its real certificate is issued.
    pub subject_alternative_name: String,
    /// How long the certificate is valid for, in days.
    pub days: u32,
    /// The key openssl generates, in its `-newkey` notation, e.g. `rsa:2048`.
    pub key_specification: String,
}
