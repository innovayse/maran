//! Tests for [`write_known_hosts`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::web::host_key::HostKey;

use super::write_known_hosts;
use crate::transfer::fake_transfer_host::FakeTransferHost;
use crate::transfer::known_hosts_alias::KNOWN_HOSTS_ALIAS;
use crate::transfer::transfer_fixtures::{HOST_ECDSA_256, HOST_ED25519};
use crate::transfer::transfer_op_error::TransferOpError;

#[test]
fn the_file_holds_exactly_the_pinned_key_under_the_fixed_alias() {
    let host = FakeTransferHost::new();
    let path = Path::new("/scratch/00/known_hosts");

    write_known_hosts(&host, path, &HostKey::parse(HOST_ED25519).unwrap()).unwrap();

    let written = host.everything_written();
    assert_eq!(written.len(), 1);
    assert_eq!(written[0].0, path);
    assert_eq!(
        String::from_utf8(written[0].1.clone()).unwrap(),
        format!("{KNOWN_HOSTS_ALIAS} {HOST_ED25519}\n")
    );
}

#[test]
fn a_different_key_gives_a_different_file() {
    let host = FakeTransferHost::new();
    let other = Path::new("/scratch/01/known_hosts");

    write_known_hosts(&host, other, &HostKey::parse(HOST_ECDSA_256).unwrap()).unwrap();

    let content = String::from_utf8(host.everything_written()[0].1.clone()).unwrap();
    assert!(content.contains(HOST_ECDSA_256));
    assert!(!content.contains(HOST_ED25519));
}

#[test]
fn an_existing_file_is_never_replaced() {
    let host = FakeTransferHost::new();
    let path = Path::new("/scratch/00/known_hosts");
    let key = HostKey::parse(HOST_ED25519).unwrap();
    write_known_hosts(&host, path, &key).unwrap();

    assert!(matches!(
        write_known_hosts(&host, path, &key),
        Err(TransferOpError::ScratchUnavailable)
    ));
}
