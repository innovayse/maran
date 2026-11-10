//! Tests for [`classify_ssh_failure`], over the client's own words.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::classify_ssh_failure;
use crate::transfer::transfer_op_error::TransferOpError;

#[test]
fn a_changed_host_key_in_each_spelling_of_the_client_is_a_changed_host_key() {
    for stderr in [
        "@@@@@@@@\n@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @\n@@@@@@@@\nHost key verification failed.\n",
        "Host key verification failed.\n",
        "No ED25519 host key is known for maran-source and you have requested strict checking.\nHost key verification failed.\n",
        "Unable to negotiate with 192.0.2.1 port 22: no matching host key type found. Their offer: ssh-rsa\n",
    ] {
        assert!(
            matches!(
                classify_ssh_failure(stderr),
                TransferOpError::HostKeyChanged
            ),
            "{stderr}"
        );
    }
}

#[test]
fn a_refused_key_is_an_authentication_refusal() {
    for stderr in [
        "root@192.0.2.1: Permission denied (publickey).\n",
        "Received disconnect from 192.0.2.1 port 22:2: Too many authentication failures\n",
        "root@192.0.2.1: Permission denied (publickey,password).\n",
    ] {
        assert!(
            matches!(classify_ssh_failure(stderr), TransferOpError::AuthRefused),
            "{stderr}"
        );
    }
}

#[test]
fn a_connection_that_never_completed_is_unreachable() {
    for stderr in [
        "ssh: connect to host 192.0.2.1 port 22: Connection refused\n",
        "ssh: connect to host 192.0.2.1 port 22: Connection timed out\n",
        "ssh: connect to host 192.0.2.1 port 22: No route to host\n",
        "kex_exchange_identification: Connection closed by remote host\n",
        "",
        "something nobody has seen before\n",
    ] {
        assert!(
            matches!(classify_ssh_failure(stderr), TransferOpError::Unreachable),
            "{stderr}"
        );
    }
}

#[test]
fn a_host_key_failure_wins_over_an_authentication_phrase() {
    let stderr = "Host key verification failed.\nPermission denied (publickey).\n";

    assert!(matches!(
        classify_ssh_failure(stderr),
        TransferOpError::HostKeyChanged
    ));
}

#[test]
fn the_classification_ignores_case() {
    assert!(matches!(
        classify_ssh_failure("PERMISSION DENIED (PUBLICKEY)."),
        TransferOpError::AuthRefused
    ));
}

#[test]
fn what_the_source_wrote_never_reaches_the_error() {
    let error = classify_ssh_failure("Permission denied (publickey). SENTINEL-FROM-THE-SOURCE");

    assert!(!format!("{error} {error:?}").contains("SENTINEL-FROM-THE-SOURCE"));
}
