//! Tests for the `invocation` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `invocation.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::{Invocation, OptionsError, USAGE};

/// The uid the caller passes in when `--allow-uid` is absent.
const DEFAULT_UID: u32 = 1000;

/// Builds an argument vector from string literals.
fn arguments(values: &[&str]) -> Vec<String> {
    values.iter().map(|value| (*value).to_owned()).collect()
}

/// The options of an invocation that is expected to run the daemon.
///
/// # Panics
///
/// Panics when the command line asked for the usage text instead, which in
/// these tests means the parse did something other than what was being checked.
fn running(invocation: Invocation) -> super::AgentOptions {
    match invocation {
        Invocation::Run(options) => options,
        other => panic!("expected a runnable invocation, got {other:?}"),
    }
}

#[test]
fn no_arguments_yields_the_production_socket_and_the_default_uid() {
    let options = running(Invocation::parse(&arguments(&[]), DEFAULT_UID).unwrap());

    assert_eq!(options.socket_path(), Path::new("/run/maran/agent.sock"));
    assert_eq!(options.allow_uid, DEFAULT_UID);
}

#[test]
fn socket_and_allow_uid_flags_are_honoured() {
    let options = running(
        Invocation::parse(
            &arguments(&["--socket", "/tmp/maran-test.sock", "--allow-uid", "4242"]),
            DEFAULT_UID,
        )
        .unwrap(),
    );

    assert_eq!(options.socket_path(), Path::new("/tmp/maran-test.sock"));
    assert_eq!(options.allow_uid, 4242);
}

#[test]
fn an_unknown_argument_is_refused_rather_than_skipped() {
    // This used to pass by IGNORING the unknown flag and honouring the rest, and
    // the consequence was observed rather than imagined: `maran-agent --help`
    // parsed as an empty command line and started a root daemon on the default
    // socket with the default uid, taking the socket from the agent already
    // serving it. A flag this binary does not define means the operator and the
    // binary disagree about what is being started, and the safe answer to that
    // is to start nothing.
    let error = Invocation::parse(
        &arguments(&["--from-a-newer-unit-file", "--allow-uid", "77"]),
        DEFAULT_UID,
    )
    .expect_err("an unrecognised flag must not parse");

    assert_eq!(
        error,
        OptionsError::UnknownFlag {
            flag: "--from-a-newer-unit-file".to_owned()
        }
    );
}

#[test]
fn asking_for_help_prints_usage_instead_of_starting_a_daemon() {
    for flag in ["--help", "-h"] {
        let invocation = Invocation::parse(&arguments(&[flag]), DEFAULT_UID)
            .unwrap_or_else(|error| panic!("{flag} must parse, got {error}"));

        assert_eq!(invocation, Invocation::ShowUsage, "{flag}");
    }

    // The text has to name both flags it documents, or it is not usage.
    assert!(USAGE.contains("--socket"));
    assert!(USAGE.contains("--allow-uid"));
}

#[test]
fn help_wins_over_a_malformed_flag_beside_it() {
    // Otherwise the one command that explains the mistake is the one refused
    // for containing it.
    let invocation = Invocation::parse(&arguments(&["--nonsense", "--help"]), DEFAULT_UID)
        .expect("help must parse even beside an unknown flag");

    assert_eq!(invocation, Invocation::ShowUsage);
}

#[test]
fn non_numeric_allow_uid_is_rejected_instead_of_falling_back() {
    let error = Invocation::parse(&arguments(&["--allow-uid", "panel"]), DEFAULT_UID)
        .expect_err("a non-numeric uid must not parse");

    assert_eq!(
        error,
        OptionsError::InvalidUid {
            value: "panel".to_owned()
        }
    );
}

#[test]
fn allow_uid_without_a_value_is_rejected_instead_of_falling_back() {
    let error = Invocation::parse(&arguments(&["--allow-uid"]), DEFAULT_UID)
        .expect_err("a dangling flag must not parse");

    assert_eq!(
        error,
        OptionsError::MissingValue {
            flag: "--allow-uid"
        }
    );
}

#[test]
fn socket_without_a_value_is_rejected() {
    let error = Invocation::parse(&arguments(&["--socket"]), DEFAULT_UID)
        .expect_err("a dangling flag must not parse");

    assert_eq!(error, OptionsError::MissingValue { flag: "--socket" });
}

#[test]
fn a_help_flag_in_a_value_position_is_still_help() {
    // Deliberate, and recorded because it is surprising: the help sweep runs over
    // the whole vector, so `--socket -h` prints usage instead of binding a socket
    // named `-h`. A socket path or a uid spelled `-h` or `--help` is not a thing
    // anyone means, and answering the question is better than binding it.
    let invocation = Invocation::parse(&arguments(&["--socket", "-h"]), DEFAULT_UID)
        .expect("a help flag anywhere must parse");

    assert_eq!(invocation, Invocation::ShowUsage);
}

#[test]
fn a_flag_used_as_another_flags_value_is_refused_rather_than_swallowed() {
    // `--socket --allow-uid 5` reads `--allow-uid` as the socket path and then
    // meets a bare `5`. The end state is safe either way — nothing starts — but
    // this pins WHICH refusal it is, so a future change that starts a daemon on
    // a socket literally named `--allow-uid` cannot pass unnoticed.
    let error = Invocation::parse(&arguments(&["--socket", "--allow-uid", "5"]), DEFAULT_UID)
        .expect_err("a flag consumed as a value must not yield a running daemon");

    assert_eq!(
        error,
        OptionsError::UnknownFlag {
            flag: "5".to_owned()
        }
    );
}

#[test]
fn a_render_subcommand_parses_its_own_flags_and_only_there() {
    // The two render subcommands are the installer's way of seeding the
    // firewall from the SAME templates the agent later applies. Both flags are
    // required and neither is defaulted: the file they produce is a
    // `policy drop` ruleset, so a port it does not name is a port nothing can
    // reach.
    let invocation = Invocation::parse(
        &arguments(&[
            "render-firewall-ruleset",
            "--ssh-port",
            "2222",
            "--panel-port",
            "8443",
        ]),
        DEFAULT_UID,
    )
    .expect("the render subcommand must parse its own flags");

    let ports = match invocation {
        Invocation::RenderFirewallRuleset(ports) => ports,
        other => panic!("expected a ruleset render, got {other:?}"),
    };
    // Asserted by FIELD, both of them, and with two different numbers. Equal
    // numbers would pass just as well if the parse put ssh's value in panel's
    // field, and that swap renders SSH's hard allow for the panel's port and
    // the panel's for SSH's — a lockout from the host and the panel at once.
    assert_eq!(
        ports
            .ssh_ports
            .iter()
            .map(|port| port.value())
            .collect::<Vec<_>>(),
        vec![2222]
    );
    assert_eq!(ports.panel_port.value(), 8443);

    // The bans table takes no parameters, and an argument means the caller
    // believes it is parameterised when it is not.
    assert_eq!(
        Invocation::parse(&arguments(&["render-firewall-bans"]), DEFAULT_UID)
            .expect("the bans render takes no flags"),
        Invocation::RenderFirewallBans
    );
    assert_eq!(
        Invocation::parse(
            &arguments(&["render-firewall-bans", "--ssh-port", "22"]),
            DEFAULT_UID
        )
        .expect_err("the bans render must refuse a port flag"),
        OptionsError::UnknownFlag {
            flag: "--ssh-port".to_owned()
        }
    );

    // And ONLY there: a subcommand is matched at the first position, so a
    // subcommand name in a value position is a value. `--socket` takes the next
    // argument whatever it spells, and what comes back is a daemon bound to a
    // strangely named socket rather than a render.
    let options = running(
        Invocation::parse(
            &arguments(&["--socket", "render-firewall-bans"]),
            DEFAULT_UID,
        )
        .expect("a subcommand name in a value position is a value"),
    );
    assert_eq!(options.socket_path(), Path::new("render-firewall-bans"));
}

#[test]
fn run_still_refuses_render_flags() {
    // The daemon's flag loop has never heard of the render flags, and this is
    // what stops `maran-agent --ssh-port 22` from starting a REAL root daemon
    // that silently ignored an argument its operator meant. The refusal is
    // asserted as the exact variant, not merely as "an error": swallowing the
    // flag and running is the failure this file's history records.
    for flag in ["--ssh-port", "--panel-port"] {
        let error = Invocation::parse(&arguments(&[flag, "22"]), DEFAULT_UID)
            .expect_err("the daemon must not accept a render flag");

        assert_eq!(
            error,
            OptionsError::UnknownFlag {
                flag: flag.to_owned()
            },
            "{flag}"
        );
    }
}

#[test]
fn a_render_subcommand_with_a_missing_port_is_refused() {
    // Absent, dangling and out of range are three ways to fail to name a port,
    // and every one of them must refuse rather than default. A defaulted 22 on
    // a host whose sshd listens elsewhere seeds a firewall that locks the
    // installing operator out of the machine they are installing on.
    let absent = Invocation::parse(
        &arguments(&["render-firewall-ruleset", "--ssh-port", "2222"]),
        DEFAULT_UID,
    )
    .expect_err("a render with only one port must be refused");
    assert_eq!(
        absent,
        OptionsError::MissingValue {
            flag: "--panel-port"
        }
    );

    let dangling = Invocation::parse(
        &arguments(&[
            "render-firewall-ruleset",
            "--panel-port",
            "8443",
            "--ssh-port",
        ]),
        DEFAULT_UID,
    )
    .expect_err("a dangling port flag must be refused");
    assert_eq!(dangling, OptionsError::MissingValue { flag: "--ssh-port" });

    // Zero is the value an absent proto field decodes to and the value a
    // firewall reads as "any port", so it is refused like any other non-port.
    for value in ["0", "65536", "ssh"] {
        let error = Invocation::parse(
            &arguments(&[
                "render-firewall-ruleset",
                "--ssh-port",
                value,
                "--panel-port",
                "8443",
            ]),
            DEFAULT_UID,
        )
        .expect_err("a value that is not a port must be refused");

        assert_eq!(
            error,
            OptionsError::InvalidPort {
                flag: "--ssh-port",
                value: value.to_owned()
            },
            "{value}"
        );
    }
}

#[test]
fn help_wins_over_a_render_subcommand() {
    // The help sweep runs over the whole command line before the subcommand is
    // matched, so somebody who cannot remember which flags the render takes can
    // ask, in the invocation they were already typing.
    let invocation = Invocation::parse(
        &arguments(&["render-firewall-ruleset", "--help"]),
        DEFAULT_UID,
    )
    .expect("help must parse beside a subcommand");

    assert_eq!(invocation, Invocation::ShowUsage);

    // And the text documents what it now accepts, or it is not usage.
    assert!(USAGE.contains("render-firewall-ruleset"));
    assert!(USAGE.contains("render-firewall-bans"));
    assert!(USAGE.contains("--ssh-port"));
    assert!(USAGE.contains("--panel-port"));
}

#[test]
fn the_ssh_port_flag_is_repeatable_and_every_occurrence_is_kept() {
    // A host can serve SSH on several ports at once — sshd listens on every
    // `Port` directive and every `ListenAddress host:port`. A flag that
    // OVERWROTE would seed a firewall opening the last one and closing the
    // others, which is the lockout the list exists to prevent.
    let invocation = Invocation::parse(
        &arguments(&[
            "render-firewall-ruleset",
            "--ssh-port",
            "2222",
            "--panel-port",
            "8443",
            "--ssh-port",
            "2022",
        ]),
        DEFAULT_UID,
    )
    .expect("the ssh port flag repeats");

    let ports = match invocation {
        Invocation::RenderFirewallRuleset(ports) => ports,
        other => panic!("expected a ruleset render, got {other:?}"),
    };
    assert_eq!(
        ports
            .ssh_ports
            .iter()
            .map(|port| port.value())
            .collect::<Vec<_>>(),
        vec![2222, 2022],
        "both ports must survive, in the order they were given"
    );
    assert_eq!(ports.panel_port.value(), 8443);
}

#[test]
fn a_repeated_panel_port_is_refused_rather_than_silently_last_wins() {
    // The asymmetry with `--ssh-port` is deliberate and is the difference
    // between the two facts: sshd really can listen on several ports, and
    // nginx's panel vhost cannot. So a second `--panel-port` means the caller
    // believes something untrue, and keeping the last value would seed a
    // firewall that opens a port the panel is not on and closes the one it is.
    let error = Invocation::parse(
        &arguments(&[
            "render-firewall-ruleset",
            "--ssh-port",
            "22",
            "--panel-port",
            "8443",
            "--panel-port",
            "9443",
        ]),
        DEFAULT_UID,
    )
    .expect_err("a repeated --panel-port must be refused");

    assert_eq!(
        error,
        OptionsError::RepeatedFlag {
            flag: "--panel-port"
        }
    );
}
