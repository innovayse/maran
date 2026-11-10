//! A freshly generated key pair onto its wire payload.

use maran_ops::transfer::model::generated_source_key::GeneratedSourceKey;

use crate::proto;

/// The wire form of a generated key pair: the public line without comment or
/// option, the private document as bytes, and the fingerprint of the public half.
///
/// This is the one place the private key leaves its [`SecretString`]: the panel
/// encrypts it at rest and never returns it, and nothing here logs it.
///
/// [`SecretString`]: maran_agent_core::secret_string::SecretString
#[must_use]
pub fn to_wire_generated_source_key(key: &GeneratedSourceKey) -> proto::GeneratedSourceKey {
    proto::GeneratedSourceKey {
        public_key: key.public_key.as_line(),
        private_key: key.private_key.expose().as_bytes().to_vec(),
        fingerprint: key.fingerprint.as_str().to_owned(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_generated_source_key_tests.rs"]
mod tests;
