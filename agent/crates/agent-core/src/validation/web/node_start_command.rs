//! The command a Node.js site is started with, split into a program and arguments.

use crate::validation::web::node_program::NodeProgram;
use crate::validation::web::node_start_command_error::NodeStartCommandError;

/// The longest start command accepted, in characters.
const MAX_LENGTH: usize = 200;

/// A validated start command — `node server.js`, `npm start` — split into the closed
/// [`NodeProgram`] and its arguments.
///
/// The command lands on the `ExecStart=` line of a per-site systemd unit that the service
/// manager runs under the account's uid. The unit is line-oriented and the service manager
/// expands `$` and `%` inside it, so the text is VALIDATED against a narrow character set and
/// then split; the program is never the caller's text at all, it is a [`NodeProgram`] that the
/// distribution adapter resolves to an absolute path. A value of this type in a signature is a
/// promise that it went through [`NodeStartCommand::parse`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NodeStartCommand {
    program: NodeProgram,
    arguments: Vec<String>,
}

impl NodeStartCommand {
    /// Parses `candidate` as `<node|npm> [argument …]`, words separated by single spaces.
    ///
    /// Arguments may use letters, digits and `. _ / @ : = + , -`. A word may not be empty, so
    /// a doubled space is refused rather than normalised.
    ///
    /// # Errors
    ///
    /// - [`NodeStartCommandError::Empty`] when there is no word at all.
    /// - [`NodeStartCommandError::TooLong`] above 200 characters.
    /// - [`NodeStartCommandError::ControlCharacter`] for any control character.
    /// - [`NodeStartCommandError::UnknownProgram`] when the first word is not `node` or `npm`.
    /// - [`NodeStartCommandError::IllegalCharacter`] for any other character outside the set.
    pub fn parse(candidate: &str) -> Result<Self, NodeStartCommandError> {
        if candidate.trim().is_empty() {
            return Err(NodeStartCommandError::Empty);
        }

        if candidate.chars().any(char::is_control) {
            return Err(NodeStartCommandError::ControlCharacter);
        }

        if candidate.chars().count() > MAX_LENGTH {
            return Err(NodeStartCommandError::TooLong { limit: MAX_LENGTH });
        }

        let mut words = candidate.split(' ');
        let program = match words.next() {
            Some("node") => NodeProgram::Node,
            Some("npm") => NodeProgram::Npm,
            _ => return Err(NodeStartCommandError::UnknownProgram),
        };

        let arguments: Vec<String> = words.map(str::to_owned).collect();
        for word in &arguments {
            if word.is_empty() {
                return Err(NodeStartCommandError::IllegalCharacter { character: ' ' });
            }
            if let Some(character) = word.chars().find(|c| !is_allowed(*c)) {
                return Err(NodeStartCommandError::IllegalCharacter { character });
            }
        }

        Ok(Self { program, arguments })
    }

    /// The program the command starts with.
    #[must_use]
    pub fn program(&self) -> NodeProgram {
        self.program
    }

    /// The validated arguments after the program.
    #[must_use]
    pub fn arguments(&self) -> &[String] {
        &self.arguments
    }
}

/// Whether `character` may appear in an argument.
fn is_allowed(character: char) -> bool {
    character.is_ascii_alphanumeric() || "._/@:=+,-".contains(character)
}

#[cfg(test)]
#[path = "../../tests/validation/web/node_start_command_tests.rs"]
mod tests;
