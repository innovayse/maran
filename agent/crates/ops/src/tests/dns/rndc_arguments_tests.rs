//! The one spelling of an `rndc` command line.

use maran_agent_core::agent_paths::AgentPaths;

use crate::dns::rndc_arguments::rndc_arguments;

#[test]
fn the_command_follows_the_key_the_loopback_address_and_the_port() {
    assert_eq!(
        rndc_arguments("953", &["status"]),
        [
            "-k",
            AgentPaths::DNS_CONTROL_KEY_PATH,
            "-s",
            "127.0.0.1",
            "-p",
            "953",
            "status"
        ]
    );
}

#[test]
fn a_command_of_several_words_keeps_its_order_after_the_connection_options() {
    let arguments = rndc_arguments("953", &["reload", "example.com"]);

    assert_eq!(arguments[6..], ["reload", "example.com"]);
}

#[test]
fn the_connection_never_names_a_non_loopback_address() {
    let arguments = rndc_arguments("953", &["status"]);

    assert_eq!(arguments[3], "127.0.0.1");
}
