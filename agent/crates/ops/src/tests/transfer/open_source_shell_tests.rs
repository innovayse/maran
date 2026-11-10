//! Tests for [`open_source_shell`]: the address check comes first, the files
//! are laid out once, and a failure leaves nothing behind.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::web::host_key::HostKey;
use maran_distro::{DistroFamily, adapter_for};

use super::open_source_shell;
use crate::transfer::fake_transfer_host::FakeTransferHost;
use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::transfer_fixtures::{HOST_ED25519, PRIVATE_KEY, credential, endpoint_at};
use crate::transfer::transfer_op_error::TransferOpError;

#[test]
fn a_forbidden_address_writes_nothing_and_sends_nothing() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["127.0.0.1"]);

    let result = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    );

    assert!(matches!(result, Err(TransferOpError::AddressRefused)));
    assert!(host.created().is_empty(), "a scratch directory was made");
    assert!(host.everything_written().is_empty(), "a file was written");
    assert!(host.runs().is_empty(), "a program was run");
}

#[test]
fn an_allowed_address_lays_out_the_key_and_the_pinned_host_key() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20"]);

    let _shell = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    )
    .unwrap();

    let written = host.everything_written();
    assert_eq!(written.len(), 2);
    assert_eq!(written[0].0.file_name().unwrap(), "key");
    assert_eq!(
        String::from_utf8(written[0].1.clone()).unwrap(),
        PRIVATE_KEY
    );
    assert_eq!(written[1].0.file_name().unwrap(), "known_hosts");
    assert_eq!(
        String::from_utf8(written[1].1.clone()).unwrap(),
        format!("maran-source {HOST_ED25519}\n")
    );
}

#[test]
fn a_key_document_parsed_without_a_final_newline_is_written_with_exactly_one() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20"]);
    let credential = SourceCredential {
        private_key: SourcePrivateKey::parse(PRIVATE_KEY.trim_end().as_bytes()).unwrap(),
        host_key: HostKey::parse(HOST_ED25519).unwrap(),
    };

    let _shell = open_source_shell(
        &host,
        adapter_for(DistroFamily::Rhel),
        &endpoint_at("source.example.org"),
        &credential,
    )
    .unwrap();

    let written = String::from_utf8(host.everything_written()[0].1.clone()).unwrap();
    assert!(written.ends_with("-----END OPENSSH PRIVATE KEY-----\n"));
    assert_eq!(
        written, PRIVATE_KEY,
        "the document changed beyond the newline"
    );
}

#[test]
fn the_key_is_on_disk_while_the_client_runs_and_gone_afterwards() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20"]);
    {
        let shell = open_source_shell(
            &host,
            adapter_for(DistroFamily::Debian),
            &endpoint_at("source.example.org"),
            &credential(),
        )
        .unwrap();
        shell.run(&RemoteCommand::EffectiveUid).unwrap();
    }

    let run = &host.runs()[0];
    let key_argument = run
        .arguments
        .iter()
        .skip_while(|argument| argument.as_str() != "-i")
        .nth(1)
        .unwrap();
    let key_present = run.files.iter().any(|(path, content)| {
        path.to_str() == Some(key_argument) && content == PRIVATE_KEY.as_bytes()
    });
    assert!(
        key_present,
        "the key file did not exist while the client ran"
    );
    assert!(host.files().is_empty(), "a file outlived the shell");
}

#[test]
fn a_failure_after_the_directory_was_made_removes_it() {
    // Occupy the first file's path so that writing the key fails: the guard
    // created before it must still remove the directory.
    let host = FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20"]);
    host.put_file(std::path::Path::new("/scratch/00/key"), "squatter");

    let result = open_source_shell(
        &host,
        adapter_for(DistroFamily::Debian),
        &endpoint_at("source.example.org"),
        &credential(),
    );

    assert!(matches!(result, Err(TransferOpError::ScratchUnavailable)));
    assert_eq!(host.removed(), host.created());
    assert!(host.everything_written().is_empty());
}
