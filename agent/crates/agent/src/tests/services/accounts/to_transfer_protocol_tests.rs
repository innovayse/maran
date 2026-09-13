//! Which daemon a suspension fact names.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::logins::LoginProtocol;

use crate::proto::TransferProtocol;
use crate::services::accounts::to_transfer_protocol::to_transfer_protocol;

#[test]
fn each_daemon_maps_to_its_own_wire_value() {
    assert_eq!(
        to_transfer_protocol(LoginProtocol::Sftp),
        TransferProtocol::Sftp
    );
    assert_eq!(
        to_transfer_protocol(LoginProtocol::Ftps),
        TransferProtocol::Ftps
    );
}

#[test]
fn the_unspecified_value_is_never_produced() {
    // On the wire it means "the agent predates this field". An agent that
    // enumerated the login knows which jail its home was in, so it always has a
    // real answer — and a caller reading Unspecified must be able to conclude it
    // is talking to an older agent, which it cannot if this one emits it too.
    for protocol in [LoginProtocol::Sftp, LoginProtocol::Ftps] {
        assert_ne!(
            to_transfer_protocol(protocol),
            TransferProtocol::Unspecified,
            "{protocol:?}"
        );
    }
}

#[test]
fn the_two_daemons_are_told_apart_and_not_collapsed() {
    // The positive control for the pair above: a mapping that answered one value
    // for both would satisfy every assertion that only checks one side.
    assert_ne!(
        to_transfer_protocol(LoginProtocol::Sftp),
        to_transfer_protocol(LoginProtocol::Ftps)
    );
}
