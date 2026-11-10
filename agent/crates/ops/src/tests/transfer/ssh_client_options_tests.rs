//! Tests for [`ssh_client_options`]: which host key algorithms the client may negotiate.

use super::negotiable_algorithms;

#[test]
fn only_the_pinned_algorithm_may_be_negotiated() {
    assert_eq!(negotiable_algorithms("ssh-ed25519"), "ssh-ed25519");
    assert_eq!(
        negotiable_algorithms("ecdsa-sha2-nistp384"),
        "ecdsa-sha2-nistp384"
    );
}
