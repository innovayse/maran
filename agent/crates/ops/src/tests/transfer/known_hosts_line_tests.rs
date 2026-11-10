//! Tests for [`known_hosts_line`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::host_key::HostKey;

use super::known_hosts_line;
use crate::transfer::known_hosts_alias::KNOWN_HOSTS_ALIAS;
use crate::transfer::transfer_fixtures::HOST_ED25519;

#[test]
fn the_line_is_the_alias_the_pinned_key_and_a_line_feed() {
    let line = known_hosts_line(&HostKey::parse(HOST_ED25519).unwrap());

    assert_eq!(line, format!("{KNOWN_HOSTS_ALIAS} {HOST_ED25519}\n"));
}
