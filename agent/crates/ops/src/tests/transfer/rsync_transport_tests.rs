//! Tests for [`rsync_transport`]: the same pinned `ssh` options, standard input
//! left open, nothing a caller typed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;
use std::path::PathBuf;

use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::port::Port;
use maran_distro::{DistroFamily, adapter_for};

use super::rsync_transport;
use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::ssh_argv::ssh_argv;

/// A connection to a public address with the key and known_hosts files in a
/// receiver directory.
fn connection() -> SshConnection {
    SshConnection {
        address: "198.51.100.20".parse::<IpAddr>().unwrap(),
        port: Port::parse(2222).unwrap(),
        user: SourceUserName::parse("root").unwrap(),
        key_file: PathBuf::from("/run/maran/transfer-receivers/ab12/key"),
        known_hosts_file: PathBuf::from("/run/maran/transfer-receivers/ab12/known_hosts"),
        host_key_algorithm: "ssh-ed25519".to_owned(),
    }
}

#[test]
fn the_transport_starts_with_the_absolute_ssh_of_the_adapter() {
    let distro = adapter_for(DistroFamily::Debian);

    let text = rsync_transport(&distro.ssh_binary(), &connection());

    assert!(
        text.starts_with(&format!("{} ", distro.ssh_binary())),
        "{text}"
    );
}

#[test]
fn every_pinned_option_the_command_runs_carry_is_in_the_transport() {
    let distro = adapter_for(DistroFamily::Debian);
    let text = rsync_transport(&distro.ssh_binary(), &connection());

    for option in [
        "StrictHostKeyChecking=yes",
        "BatchMode=yes",
        "IdentitiesOnly=yes",
        "GlobalKnownHostsFile=/dev/null",
        "HostKeyAlias=maran-source",
        "HostKeyAlgorithms=ssh-ed25519",
        "UpdateHostKeys=no",
        "ClearAllForwardings=yes",
        "ProxyCommand=none",
        "-F /dev/null",
        "-i /run/maran/transfer-receivers/ab12/key",
        "-p 2222",
        "UserKnownHostsFile=/run/maran/transfer-receivers/ab12/known_hosts",
    ] {
        assert!(text.contains(option), "{option} missing from {text}");
    }
}

#[test]
fn the_standard_input_is_left_open_because_the_protocol_runs_over_it() {
    let distro = adapter_for(DistroFamily::Debian);
    let transport = rsync_transport(&distro.ssh_binary(), &connection());
    let command_argv = ssh_argv(&connection(), "id -u");

    // Inverse control: the command-running vector closes stdin with `-n`, the
    // transport must not.
    assert!(command_argv.contains(&"-n".to_owned()));
    assert!(
        !transport.split(' ').any(|part| part == "-n"),
        "{transport}"
    );
}

#[test]
fn no_element_of_the_transport_holds_a_space_inside_a_value() {
    // rsync splits the value on white space: a value with a space would be two
    // arguments. The elements joined by one space each must re-split to the same
    // number of parts the options had.
    let distro = adapter_for(DistroFamily::Debian);
    let text = rsync_transport(&distro.ssh_binary(), &connection());
    let options = crate::transfer::ssh_client_options::ssh_client_options(&connection(), false);

    assert_eq!(text.split(' ').count(), options.len() + 1);
}
