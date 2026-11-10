//! Tests for [`ssh_argv`]: the pinned options are on every invocation and the
//! destination is the resolved address.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::port::Port;

use super::ssh_argv;
use crate::transfer::model::ssh_connection::SshConnection;

/// A connection to `address` pinned to `algorithm`.
fn connection(address: &str, algorithm: &str) -> SshConnection {
    SshConnection {
        address: address.parse().unwrap(),
        port: Port::parse(2222).unwrap(),
        user: SourceUserName::parse("root").unwrap(),
        key_file: PathBuf::from("/scratch/00/key"),
        known_hosts_file: PathBuf::from("/scratch/00/known_hosts"),
        host_key_algorithm: algorithm.to_owned(),
    }
}

/// Whether the argv carries `-o <option>`.
fn has_option(argv: &[String], option: &str) -> bool {
    argv.windows(2)
        .any(|pair| pair[0] == "-o" && pair[1] == option)
}

#[test]
fn every_option_of_the_threat_notes_section_three_is_set() {
    let argv = ssh_argv(&connection("192.0.2.1", "ssh-ed25519"), "id -u");

    assert_eq!(&argv[..2], ["-F", "/dev/null"]);
    for option in [
        "BatchMode=yes",
        "IdentitiesOnly=yes",
        "StrictHostKeyChecking=yes",
        "UserKnownHostsFile=/scratch/00/known_hosts",
        "GlobalKnownHostsFile=/dev/null",
        "HostKeyAlias=maran-source",
        "HostKeyAlgorithms=ssh-ed25519",
        "CheckHostIP=no",
        "UpdateHostKeys=no",
        "ConnectTimeout=15",
        "ServerAliveInterval=15",
        "ServerAliveCountMax=4",
        "LogLevel=ERROR",
        "ForwardAgent=no",
        "ForwardX11=no",
        "ClearAllForwardings=yes",
        "PasswordAuthentication=no",
        "PubkeyAuthentication=yes",
        "PreferredAuthentications=publickey",
        "ControlMaster=no",
        "ControlPath=none",
        "PermitLocalCommand=no",
        "ProxyCommand=none",
        "ProxyJump=none",
    ] {
        assert!(has_option(&argv, option), "missing -o {option}: {argv:?}");
    }
}

#[test]
fn the_key_the_port_and_the_resolved_address_are_passed_and_the_command_is_last() {
    let argv = ssh_argv(&connection("192.0.2.1", "ssh-ed25519"), "id -u");

    let key = argv.iter().position(|part| part == "-i").unwrap();
    assert_eq!(argv[key + 1], "/scratch/00/key");
    let port = argv.iter().position(|part| part == "-p").unwrap();
    assert_eq!(argv[port + 1], "2222");
    assert_eq!(argv[argv.len() - 3], "--");
    assert_eq!(argv[argv.len() - 2], "root@192.0.2.1");
    assert_eq!(argv[argv.len() - 1], "id -u");
}

#[test]
fn an_ipv6_destination_is_the_literal_address() {
    let argv = ssh_argv(&connection("2001:db8::5", "ssh-ed25519"), "id -u");

    assert_eq!(argv[argv.len() - 2], "root@2001:db8::5");
}

#[test]
fn the_command_is_one_argument_whatever_it_holds() {
    let command = "PATH=x; { cat -- '/a b'; } 2>/dev/null";
    let argv = ssh_argv(&connection("192.0.2.1", "ssh-ed25519"), command);

    assert_eq!(
        argv.iter().filter(|part| part.as_str() == command).count(),
        1
    );
    assert_eq!(argv.last().unwrap(), command);
}

#[test]
fn an_rsa_key_is_negotiated_through_its_sha2_signatures_not_the_sha1_one() {
    let argv = ssh_argv(&connection("192.0.2.1", "ssh-rsa"), "id -u");

    assert!(has_option(
        &argv,
        "HostKeyAlgorithms=rsa-sha2-512,rsa-sha2-256"
    ));
    assert!(!argv.iter().any(|part| part == "HostKeyAlgorithms=ssh-rsa"));
}

#[test]
fn nothing_that_disables_host_key_checking_is_ever_set() {
    for algorithm in ["ssh-ed25519", "ssh-rsa"] {
        let argv = ssh_argv(&connection("192.0.2.1", algorithm), "id -u");

        for forbidden in [
            "StrictHostKeyChecking=no",
            "StrictHostKeyChecking=accept-new",
            "UserKnownHostsFile=/dev/null",
        ] {
            assert!(!has_option(&argv, forbidden), "{forbidden}");
        }
    }
}
