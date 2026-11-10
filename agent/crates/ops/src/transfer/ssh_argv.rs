//! The ONE builder of the argument vector every `ssh` run uses.

use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::ssh_client_options::ssh_client_options;

/// The arguments of one `ssh` run of `command` on the connection.
///
/// The options are [`ssh_client_options`] with the standard input closed; the
/// destination is the RESOLVED address, never the name. `command` is the one
/// remote command string, from
/// [`crate::transfer::remote_command::RemoteCommand::render`]; it is a single
/// argument after the destination. Each returned element is one `execve`
/// argument; no shell on this host parses any of it.
#[must_use]
pub fn ssh_argv(connection: &SshConnection, command: &str) -> Vec<String> {
    let mut argv = ssh_client_options(connection, true);

    argv.push("--".to_owned());
    argv.push(format!(
        "{}@{}",
        connection.user.as_str(),
        connection.address
    ));
    argv.push(command.to_owned());

    argv
}

#[cfg(test)]
#[path = "../tests/transfer/ssh_argv_tests.rs"]
mod tests;
