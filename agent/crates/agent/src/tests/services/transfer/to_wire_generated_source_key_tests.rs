//! Tests for a freshly generated key pair on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;
use maran_ops::transfer::GeneratedSourceKey;

use super::to_wire_generated_source_key;

/// An inert public key line (a published example key).
const PUBLIC: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG7OpD9/qnELjIQS7Z+hv9E4OBM+3IqncDtZr2x3fz0s";

/// An inert private key document: header, magic-bearing body, footer.
const PRIVATE: &str = "-----BEGIN OPENSSH PRIVATE KEY-----\n\
    b3BlbnNzaC1rZXktdjEACQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ\n\
    -----END OPENSSH PRIVATE KEY-----\n";

fn generated() -> GeneratedSourceKey {
    GeneratedSourceKey {
        public_key: SshPublicKey::parse(PUBLIC).unwrap(),
        private_key: SourcePrivateKey::parse(PRIVATE.as_bytes()).unwrap(),
        fingerprint: HostKeyFingerprint::from_digest(&[5; 32]),
    }
}

#[test]
fn the_public_line_the_private_document_and_the_fingerprint_are_carried() {
    let key = generated();

    let wire = to_wire_generated_source_key(&key);

    assert_eq!(wire.public_key, PUBLIC);
    assert_eq!(wire.private_key, key.private_key.expose().as_bytes());
    assert_eq!(wire.fingerprint, key.fingerprint.as_str());
}

#[test]
fn the_public_half_carries_no_comment_and_no_option() {
    let wire = to_wire_generated_source_key(&generated());

    assert_eq!(wire.public_key.split(' ').count(), 2, "{}", wire.public_key);
    assert!(wire.public_key.starts_with("ssh-ed25519 "));
}
