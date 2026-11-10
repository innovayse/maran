//! Tests for [`scan_host_key`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use super::scan_host_key;
use crate::transfer::fake_transfer_host::{FakeTransferHost, Responder, outcome};
use crate::transfer::transfer_fixtures::{
    HOST_ECDSA_256, HOST_ED25519, HOST_ED25519_FINGERPRINT, endpoint_at,
};
use crate::transfer::transfer_op_error::TransferOpError;

/// A host for a source at a public address, answering as `responder`.
fn host_answering(responder: Responder) -> FakeTransferHost {
    FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20"])
        .answering(responder)
}

#[test]
fn the_keys_the_source_offers_come_back_with_their_fingerprints() {
    let output = format!("198.51.100.20 {HOST_ECDSA_256}\n198.51.100.20 {HOST_ED25519}\n");
    let host = host_answering(Box::new(move |_, _| Ok(outcome(0, &output, "# banner\n"))));

    let keys = scan_host_key(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
    )
    .unwrap();

    assert_eq!(keys.len(), 2);
    assert_eq!(keys[0].key.as_str(), HOST_ED25519);
    assert_eq!(keys[0].fingerprint.as_str(), HOST_ED25519_FINGERPRINT);
}

#[test]
fn the_scan_connects_to_the_resolved_literal_with_the_families_keyscan() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel, DistroFamily::Suse] {
        let distro = adapter_for(family);
        let host = host_answering(Box::new(|_, _| {
            Ok(outcome(0, &format!("h {HOST_ED25519}\n"), ""))
        }));

        scan_host_key(&host, distro, &endpoint_at("source.example.org")).unwrap();

        let runs = host.runs();
        assert_eq!(runs.len(), 1);
        assert_eq!(runs[0].program, distro.ssh_keyscan_binary().as_path());
        assert_eq!(runs[0].class, CallClass::Query);
        assert_eq!(
            runs[0].arguments,
            [
                "-T",
                "10",
                "-t",
                "ed25519,ecdsa,rsa",
                "-p",
                "22",
                "--",
                "198.51.100.20"
            ]
        );
        assert!(
            host.everything_written().is_empty(),
            "a scan needs no credential on disk"
        );
        assert!(host.created().is_empty());
    }
}

#[test]
fn a_forbidden_resolved_address_sends_nothing() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["10.0.0.1", "127.0.0.1"]);

    let result = scan_host_key(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
    );

    assert!(matches!(result, Err(TransferOpError::AddressRefused)));
    assert!(host.runs().is_empty());
}

#[test]
fn a_host_that_offers_nothing_or_fails_is_unreachable() {
    for answer in [
        outcome(0, "", "# banner only\n"),
        outcome(1, "", ""),
        outcome(0, "# x\n\n", ""),
    ] {
        let host = host_answering(Box::new(move |_, _| Ok(answer.clone())));

        let result = scan_host_key(
            &host,
            adapter_for(DistroFamily::Debian),
            &endpoint_at("source.example.org"),
        );

        assert!(matches!(result, Err(TransferOpError::Unreachable)));
    }
}

#[test]
fn printed_lines_that_are_no_acceptable_key_are_an_invalid_answer() {
    let host = host_answering(Box::new(|_, _| {
        Ok(outcome(
            0,
            "h ssh-dss AAAAB3NzaC1kc3MAAACB\nnot a key\n",
            "",
        ))
    }));

    let result = scan_host_key(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
    );

    assert!(matches!(result, Err(TransferOpError::InvalidRemoteOutput)));
}
