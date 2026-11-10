//! The pinned options every `ssh` run on a migration source carries.

use crate::transfer::known_hosts_alias::KNOWN_HOSTS_ALIAS;
use crate::transfer::model::ssh_connection::SshConnection;

/// Seconds the client waits for the TCP and SSH handshake.
const CONNECT_TIMEOUT_SECONDS: u32 = 15;

/// Seconds between keep-alive probes of an established connection.
const ALIVE_INTERVAL_SECONDS: u32 = 15;

/// Unanswered keep-alive probes after which the connection is dropped.
const ALIVE_COUNT_MAX: u32 = 4;

/// The host key algorithms the client may negotiate for a pinned key of
/// `algorithm`: exactly that one. The RSA key is negotiated through its SHA-2
/// signature algorithms, since current clients no longer accept `ssh-rsa` as a
/// signature; the key blob in the known_hosts file stays `ssh-rsa`.
fn negotiable_algorithms(algorithm: &str) -> String {
    match algorithm {
        "ssh-rsa" => "rsa-sha2-512,rsa-sha2-256".to_owned(),
        other => other.to_owned(),
    }
}

/// The options of every `ssh` run on `connection`, up to but not including the
/// destination and the command.
///
/// The only place the pinned options of the threat note's section 3 are
/// written, so they are set on EVERY invocation and cannot be forgotten by a new
/// caller: no configuration file is read (`-F /dev/null`, and the global
/// known_hosts is pointed at nothing), no prompt is possible (`BatchMode`, and the
/// public-key method is the only one preferred), only the one key file is offered
/// (`IdentitiesOnly`), the host key must equal the pinned one
/// (`StrictHostKeyChecking=yes` against the agent's own file, under a fixed
/// alias, for the one pinned algorithm; `UpdateHostKeys=no` so the file is never
/// extended), nothing is forwarded, no terminal is allocated, and the connection
/// is bounded in time.
///
/// `stdin_closed` adds `-n`: a remote command that reads nothing (every command
/// of the closed enum) has its standard input redirected from the null device. A
/// transport that carries a protocol over the connection (the `rsync` the
/// transfer area runs) needs its standard input and passes `false`.
#[must_use]
pub fn ssh_client_options(connection: &SshConnection, stdin_closed: bool) -> Vec<String> {
    let mut argv: Vec<String> = Vec::new();
    let mut push = |value: String| argv.push(value);

    push("-F".to_owned());
    push("/dev/null".to_owned());
    if stdin_closed {
        push("-n".to_owned());
    }
    push("-T".to_owned());
    for option in [
        "BatchMode=yes".to_owned(),
        "IdentitiesOnly=yes".to_owned(),
        "StrictHostKeyChecking=yes".to_owned(),
        format!(
            "UserKnownHostsFile={}",
            connection.known_hosts_file.display()
        ),
        "GlobalKnownHostsFile=/dev/null".to_owned(),
        format!("HostKeyAlias={KNOWN_HOSTS_ALIAS}"),
        format!(
            "HostKeyAlgorithms={}",
            negotiable_algorithms(&connection.host_key_algorithm)
        ),
        "CheckHostIP=no".to_owned(),
        "UpdateHostKeys=no".to_owned(),
        format!("ConnectTimeout={CONNECT_TIMEOUT_SECONDS}"),
        format!("ServerAliveInterval={ALIVE_INTERVAL_SECONDS}"),
        format!("ServerAliveCountMax={ALIVE_COUNT_MAX}"),
        "LogLevel=ERROR".to_owned(),
        "ForwardAgent=no".to_owned(),
        "ForwardX11=no".to_owned(),
        "ClearAllForwardings=yes".to_owned(),
        "PasswordAuthentication=no".to_owned(),
        "PubkeyAuthentication=yes".to_owned(),
        "PreferredAuthentications=publickey".to_owned(),
        "ControlMaster=no".to_owned(),
        "ControlPath=none".to_owned(),
        "PermitLocalCommand=no".to_owned(),
        "ProxyCommand=none".to_owned(),
        "ProxyJump=none".to_owned(),
    ] {
        push("-o".to_owned());
        push(option);
    }
    push("-i".to_owned());
    push(connection.key_file.display().to_string());
    push("-p".to_owned());
    push(connection.port.value().to_string());

    argv
}

#[cfg(test)]
#[path = "../tests/transfer/ssh_client_options_tests.rs"]
mod tests;
