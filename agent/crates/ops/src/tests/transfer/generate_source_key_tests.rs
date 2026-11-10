//! Tests for [`generate_source_key`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use super::generate_source_key;
use crate::transfer::fake_transfer_host::{FakeTransferHost, Responder, outcome};
use crate::transfer::transfer_fixtures::{
    HOST_ECDSA_256, PRIVATE_KEY, PRIVATE_KEY_SENTINEL, PUBLIC_KEY_FILE, PUBLIC_KEY_FINGERPRINT,
    PUBLIC_KEY_LINE,
};
use crate::transfer::transfer_op_error::TransferOpError;

/// A responder that plays `ssh-keygen`: it writes the two files named by `-f`.
fn keygen_writing(private: &'static str, public: &'static str, status: i32) -> Responder {
    Box::new(move |host, run| {
        let target = run
            .arguments
            .iter()
            .skip_while(|argument| argument.as_str() != "-f")
            .nth(1)
            .unwrap();
        host.put_file(Path::new(target), private);
        host.put_file(Path::new(&format!("{target}.pub")), public);

        Ok(outcome(status, "", ""))
    })
}

#[test]
fn a_key_pair_comes_back_with_the_fingerprint_ssh_prints() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel, DistroFamily::Suse] {
        let distro = adapter_for(family);
        let host =
            FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, PUBLIC_KEY_FILE, 0));

        let key = generate_source_key(&host, distro).unwrap();

        assert_eq!(key.public_key.as_line(), PUBLIC_KEY_LINE);
        assert_eq!(key.fingerprint.as_str(), PUBLIC_KEY_FINGERPRINT);
        assert_eq!(key.private_key.expose(), PRIVATE_KEY);
    }
}

#[test]
fn the_keygen_is_run_with_no_passphrase_no_comment_and_an_ed25519_key_in_the_scratch() {
    let distro = adapter_for(DistroFamily::Debian);
    let host = FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, PUBLIC_KEY_FILE, 0));

    generate_source_key(&host, distro).unwrap();

    let runs = host.runs();
    assert_eq!(runs.len(), 1);
    assert_eq!(runs[0].program, distro.ssh_keygen_binary().as_path());
    assert_eq!(runs[0].class, CallClass::Mutation);
    assert_eq!(
        runs[0].arguments,
        [
            "-q",
            "-t",
            "ed25519",
            "-N",
            "",
            "-C",
            "",
            "-f",
            "/scratch/00/key"
        ]
    );
}

#[test]
fn the_scratch_is_removed_on_success_and_nothing_survives() {
    let host = FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, PUBLIC_KEY_FILE, 0));

    generate_source_key(&host, adapter_for(DistroFamily::Debian)).unwrap();

    assert_eq!(host.removed(), host.created());
    assert_eq!(host.created().len(), 1);
    assert!(host.files().is_empty(), "key material outlived the call");
}

#[test]
fn the_scratch_is_removed_on_every_failure_too() {
    let broken: [(&str, Responder); 4] = [
        (
            "keygen failed",
            keygen_writing(PRIVATE_KEY, PUBLIC_KEY_FILE, 1),
        ),
        (
            "private half malformed",
            keygen_writing("not a key\n", PUBLIC_KEY_FILE, 0),
        ),
        (
            "public half malformed",
            keygen_writing(PRIVATE_KEY, "garbage\n", 0),
        ),
        (
            "files never written",
            Box::new(|_, _| Ok(outcome(0, "", ""))),
        ),
    ];

    for (name, responder) in broken {
        let host = FakeTransferHost::new().answering(responder);

        let result = generate_source_key(&host, adapter_for(DistroFamily::Rhel));

        assert!(
            matches!(result, Err(TransferOpError::KeyGenerationFailed)),
            "{name}: {result:?}"
        );
        assert_eq!(host.removed(), host.created(), "{name}");
        assert!(
            host.files().is_empty(),
            "{name}: key material outlived the call"
        );
    }
}

#[test]
fn a_public_half_of_another_algorithm_is_refused() {
    let host = FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, HOST_ECDSA_256, 0));

    assert!(matches!(
        generate_source_key(&host, adapter_for(DistroFamily::Debian)),
        Err(TransferOpError::KeyGenerationFailed)
    ));
}

#[test]
fn the_private_key_is_in_no_argument_vector_and_no_error() {
    let host = FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, "garbage\n", 0));

    let error = generate_source_key(&host, adapter_for(DistroFamily::Debian)).unwrap_err();

    assert!(!format!("{error} {error:?}").contains(PRIVATE_KEY_SENTINEL));
    for run in host.runs() {
        assert!(
            run.arguments
                .iter()
                .all(|argument| !argument.contains(PRIVATE_KEY_SENTINEL))
        );
    }
}

#[test]
fn a_generated_key_never_prints_its_private_half() {
    let host = FakeTransferHost::new().answering(keygen_writing(PRIVATE_KEY, PUBLIC_KEY_FILE, 0));

    let key = generate_source_key(&host, adapter_for(DistroFamily::Debian)).unwrap();

    assert!(!format!("{key:?}").contains(PRIVATE_KEY_SENTINEL));
    // Positive control: the sentinel IS in the value, behind the one reader.
    assert!(key.private_key.expose().contains(PRIVATE_KEY_SENTINEL));
}
