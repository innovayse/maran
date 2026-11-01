//! Tests for [`AuthorizedKeySet`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;

use super::AuthorizedKeySet;
use crate::sftp::SftpError;

const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIG7OpD9/qnELjIQS7Z+hv9E4OBM+3IqncDtZr2x3fz0s";

/// An Ed25519 key whose 32 key bytes are all `byte`.
fn key(byte: u8) -> SshPublicKey {
    use base64::Engine as _;
    let mut blob = Vec::new();
    for field in [b"ssh-ed25519".as_slice(), &[byte; 32]] {
        blob.extend_from_slice(&u32::try_from(field.len()).unwrap().to_be_bytes());
        blob.extend_from_slice(field);
    }
    let data = base64::engine::general_purpose::STANDARD.encode(blob);
    SshPublicKey::parse(&format!("ssh-ed25519 {data}")).unwrap()
}

#[test]
fn a_key_supplied_twice_is_held_once() {
    let parsed = SshPublicKey::parse(ED25519).unwrap();
    let set = AuthorizedKeySet::new(vec![parsed.clone(), parsed]).unwrap();

    assert_eq!(set.len(), 1);
    assert_eq!(set.lines(), vec![ED25519.to_owned()]);
}

#[test]
fn the_ceiling_is_accepted_and_one_more_is_refused() {
    let at_ceiling: Vec<SshPublicKey> = (0..32).map(key).collect();
    assert_eq!(AuthorizedKeySet::new(at_ceiling).unwrap().len(), 32);

    let over: Vec<SshPublicKey> = (0..33).map(key).collect();
    assert!(matches!(
        AuthorizedKeySet::new(over),
        Err(SftpError::TooManyKeys { count: 33 })
    ));
}

#[test]
fn an_empty_set_is_empty() {
    assert!(AuthorizedKeySet::new(Vec::new()).unwrap().is_empty());
}
