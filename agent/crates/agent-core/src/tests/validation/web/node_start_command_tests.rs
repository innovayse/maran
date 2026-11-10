//! Tests for the `node_start_command` module. The command reaches a root-managed unit file,
//! so the set of things it refuses is the point.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::node_program::NodeProgram;
use super::super::node_start_command::NodeStartCommand;
use super::super::node_start_command_error::NodeStartCommandError;

#[test]
fn a_node_script_command_parses_into_program_and_arguments() {
    let command = NodeStartCommand::parse("node server.js --port=3000").unwrap();

    assert_eq!(command.program(), NodeProgram::Node);
    assert_eq!(command.arguments(), ["server.js", "--port=3000"]);
}

#[test]
fn npm_start_parses() {
    let command = NodeStartCommand::parse("npm start").unwrap();

    assert_eq!(command.program(), NodeProgram::Npm);
    assert_eq!(command.arguments(), ["start"]);
}

#[test]
fn a_command_with_a_newline_is_rejected() {
    let refused = NodeStartCommand::parse("node a.js\nExecStartPre=/bin/sh");

    assert_eq!(refused, Err(NodeStartCommandError::ControlCharacter));
}

#[test]
fn another_program_is_rejected() {
    assert_eq!(
        NodeStartCommand::parse("/bin/sh -c id"),
        Err(NodeStartCommandError::UnknownProgram)
    );
}

#[test]
fn a_program_that_only_starts_like_node_is_rejected() {
    assert_eq!(
        NodeStartCommand::parse("nodejs a.js"),
        Err(NodeStartCommandError::UnknownProgram)
    );
}

#[test]
fn variable_and_specifier_expansion_is_rejected() {
    for text in [
        "node $HOME/a.js",
        "node a-%u.js",
        "node `id`",
        "node a.js;id",
        "node a.js&&id",
    ] {
        assert!(
            matches!(
                NodeStartCommand::parse(text),
                Err(NodeStartCommandError::IllegalCharacter { .. })
            ),
            "{text} was accepted"
        );
    }
}

#[test]
fn a_doubled_space_is_rejected() {
    assert!(matches!(
        NodeStartCommand::parse("node  a.js"),
        Err(NodeStartCommandError::IllegalCharacter { .. })
    ));
}

#[test]
fn an_empty_command_is_rejected() {
    assert_eq!(
        NodeStartCommand::parse("  "),
        Err(NodeStartCommandError::Empty)
    );
}

#[test]
fn an_overlong_command_is_rejected() {
    let long = format!("node {}", "a".repeat(200));

    assert!(matches!(
        NodeStartCommand::parse(&long),
        Err(NodeStartCommandError::TooLong { .. })
    ));
}
