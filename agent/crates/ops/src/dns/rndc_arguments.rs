//! The one spelling of an `rndc` command line.

use maran_agent_core::agent_paths::AgentPaths;

/// The loopback address every `rndc` run connects to; the daemon's control
/// channel is bound there and nowhere else.
const CONTROL_ADDRESS: &str = "127.0.0.1";

/// Builds the argument vector of one `rndc` run: the key file, the loopback
/// address and port of the control channel, then `command`.
///
/// `port` is the decimal spelling of `AgentPaths::DNS_CONTROL_PORT`, passed in
/// because a borrowed `&str` cannot be made from a `u16` here and the caller
/// holds the string for the length of the spawn. `command` is built by the
/// caller from constants and validated types only; this function adds nothing a
/// request could influence, and it is the single place the `-k`, `-s` and `-p`
/// options are spelled, so no area writes its own copy.
#[must_use]
pub fn rndc_arguments<'a>(port: &'a str, command: &[&'a str]) -> Vec<&'a str> {
    let mut arguments = vec![
        "-k",
        AgentPaths::DNS_CONTROL_KEY_PATH,
        "-s",
        CONTROL_ADDRESS,
        "-p",
        port,
    ];
    arguments.extend_from_slice(command);
    arguments
}

#[cfg(test)]
#[path = "../tests/dns/rndc_arguments_tests.rs"]
mod tests;
