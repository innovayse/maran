//! Tests for the blocking body of a `ReadSourceInventory` stream.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fake_transfer_host.rs"]
mod fake_transfer_host;

use std::net::IpAddr;

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_distro::{DistroFamily, adapter_for};
use maran_ops::transfer::inventory::model::inventory_sink::InventorySink;
use maran_ops::transfer::inventory::model::inventory_step::InventoryStep;
use maran_ops::transfer::inventory::model::source_account_record::SourceAccountRecord;
use maran_ops::transfer::inventory::model::source_warning_record::SourceWarningRecord;
use maran_ops::transfer::{SourceCredential, SourceEndpoint, TransferOpError};

use super::read_inventory;
use fake_transfer_host::FakeTransferHost;

/// The base64 text of the marker bytes inside the key document: what the scratch
/// file holds and nothing else should.
const KEY_MARKER: &str = "S0VZTUFSS0VS";

/// A sink that takes everything and keeps nothing: these tests look at the host.
struct DiscardingSink;

impl InventorySink for DiscardingSink {
    fn progress(&mut self, _step: InventoryStep) {}

    fn account(&mut self, _account: SourceAccountRecord) {}

    fn warning(&mut self, _warning: SourceWarningRecord) {}
}

fn endpoint(host: &str) -> SourceEndpoint {
    SourceEndpoint {
        host: RemoteHost::parse(host).unwrap(),
        port: Port::parse(22).unwrap(),
        user: SourceUserName::parse("root").unwrap(),
    }
}

fn credential() -> SourceCredential {
    // base64 of `openssh-key-v1\0` followed by the marker bytes.
    let body = "b3BlbnNzaC1rZXktdjEAS0VZTUFSS0VS";
    let document =
        format!("-----BEGIN OPENSSH PRIVATE KEY-----\n{body}\n-----END OPENSSH PRIVATE KEY-----\n");

    SourceCredential {
        private_key: SourcePrivateKey::parse(document.as_bytes()).unwrap(),
        host_key: HostKey::parse(
            "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH",
        )
        .unwrap(),
    }
}

fn read(host: &FakeTransferHost, endpoint: &SourceEndpoint) -> Result<(), TransferOpError> {
    let distro = adapter_for(DistroFamily::Debian);
    let mut sink = DiscardingSink;

    read_inventory(host, distro, endpoint, &credential(), &mut sink).map(|_| ())
}

#[test]
fn a_refused_key_ends_the_read_and_removes_the_scratch_that_held_it() {
    let host = FakeTransferHost::new(
        255,
        "root@192.0.2.10: Permission denied (publickey).",
        vec![],
    );

    let failure = read(&host, &endpoint("192.0.2.10")).unwrap_err();

    assert!(matches!(failure, TransferOpError::AuthRefused), "{failure}");
    // Positive control: the key really was written to the scratch before the
    // client ran, so "gone afterwards" below is a statement about something.
    assert!(host.ever_wrote(KEY_MARKER.as_bytes()));
    assert_eq!(host.created().len(), 1);
    assert_eq!(host.removed(), host.created());
}

#[test]
fn another_failure_of_the_client_is_told_apart_and_cleans_up_the_same_way() {
    // Inverse control: the same setup with a different client message is a
    // different error, so the test above is not satisfied by "any failure".
    let host = FakeTransferHost::new(
        255,
        "ssh: connect to host 192.0.2.10 port 22: Connection timed out",
        vec![],
    );

    let failure = read(&host, &endpoint("192.0.2.10")).unwrap_err();

    assert!(matches!(failure, TransferOpError::Unreachable), "{failure}");
    assert_eq!(host.removed(), host.created());
}

#[test]
fn a_name_that_resolves_to_a_forbidden_address_is_refused_before_a_key_is_written() {
    let loopback: IpAddr = "127.0.0.1".parse().unwrap();
    let host = FakeTransferHost::new(0, "", vec![loopback]);

    let failure = read(&host, &endpoint("source.example.com")).unwrap_err();

    assert!(
        matches!(failure, TransferOpError::AddressRefused),
        "{failure}"
    );
    assert!(
        host.created().is_empty(),
        "no scratch for a refused address"
    );
    assert!(
        !host.ever_wrote(KEY_MARKER.as_bytes()),
        "the key was never written"
    );
    assert_eq!(host.runs(), 0, "nothing was sent");
}
