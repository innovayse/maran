//! The remote shell `rsync` is told to use.

use maran_distro::AdapterBinary;

use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::ssh_client_options::ssh_client_options;

/// The `--rsh` value that makes `rsync` connect through the same pinned `ssh`
/// every other call of this area uses.
///
/// `rsync` splits the value on white space into the program and its arguments
/// and runs it without a shell, so the text is the absolute path of `ssh` from
/// the distro adapter followed by [`ssh_client_options`] with the standard input
/// left open (the rsync protocol runs over it). No element contains a space: the
/// paths are built by the agent from a random hexadecimal directory name, the
/// alias and algorithm names are constants, and the port is a number. The
/// destination and the remote command are not part of it; `rsync` appends them.
#[must_use]
pub fn rsync_transport(ssh: &AdapterBinary, connection: &SshConnection) -> String {
    let mut parts = vec![ssh.to_string()];
    parts.extend(ssh_client_options(connection, false));

    parts.join(" ")
}

#[cfg(test)]
#[path = "../tests/transfer/rsync_transport_tests.rs"]
mod tests;
