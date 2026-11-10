//! The ONE builder of the argument vector every `rsync` run uses.

use std::net::IpAddr;

use maran_agent_core::agent_paths::AgentPaths;

use crate::transfer::model::pull_phase::PullPhase;
use crate::transfer::model::pull_request::PullRequest;

/// The largest single file `rsync` is allowed to receive (`--max-size`). A file
/// over it is skipped and the run ends with the partial-transfer status, which the
/// pull reports as a failure; a total ceiling is the account's disk quota, which
/// the receiving uid cannot exceed.
const MAX_FILE_SIZE: &str = "64G";

/// Seconds `rsync` waits for any I/O on the connection before it gives up.
const IO_TIMEOUT_SECONDS: u32 = 600;

/// The arguments of one `rsync` run that copies the source's home into the
/// account's home.
///
/// The only place an `rsync` argv is built, so the controls of the threat note's
/// section 1 are set on EVERY run: only regular files, directories and links are
/// copied (`--no-devices`, `--no-specials`; hard links are not preserved, since
/// `--hard-links` is never passed), a link that points outside the transferred
/// tree is not copied (`--safe-links`), setuid, setgid and the sticky bit are
/// cleared from every received mode (`--chmod=a-s,a-t`), one file is bounded
/// (`--max-size`), the connection is bounded in idle time (`--timeout`), and the
/// transport is the pinned `ssh` of [`crate::transfer::rsync_transport`]. A
/// partial file is kept (`--partial`), so the run that follows a killed one
/// continues instead of starting over. The final phase also deletes
/// (`--delete`); the initial phase never does.
///
/// The source is the RESOLVED address, never the name. Nothing in the vector
/// comes from a caller as text: the home is a validated remote path, the login a
/// validated name, the limit a validated figure and the destination is built from
/// a validated account name. `--` ends the options before the two operands.
#[must_use]
pub fn rsync_argv(request: &PullRequest, transport: &str, address: IpAddr) -> Vec<String> {
    let mut argv: Vec<String> = Vec::new();

    for option in [
        "--recursive",
        "--links",
        "--times",
        "--perms",
        "--omit-dir-times",
        "--no-devices",
        "--no-specials",
        "--safe-links",
        "--chmod=a-s,a-t",
        "--partial",
        "--no-motd",
    ] {
        argv.push(option.to_owned());
    }
    argv.push(format!("--max-size={MAX_FILE_SIZE}"));
    argv.push(format!("--timeout={IO_TIMEOUT_SECONDS}"));
    argv.push(format!("--rsh={transport}"));
    if let Some(limit) = request.bandwidth {
        argv.push(limit.as_argument());
    }
    if request.phase == PullPhase::Final {
        argv.push("--delete".to_owned());
    }
    argv.push("--".to_owned());
    argv.push(source_operand(request, address));
    argv.push(destination_operand(request));

    argv
}

/// `user@host:/home/alice/`: the contents of the source home, not the directory
/// itself. An IPv6 literal is bracketed, as `rsync` requires.
fn source_operand(request: &PullRequest, address: IpAddr) -> String {
    let host = match address {
        IpAddr::V4(v4) => v4.to_string(),
        IpAddr::V6(v6) => format!("[{v6}]"),
    };
    let home = request.source_home.as_str();
    let slash = if home.ends_with('/') { "" } else { "/" };

    format!("{}@{host}:{home}{slash}", request.endpoint.user.as_str())
}

/// `/home/<account>/`: the contents land inside the account's home.
fn destination_operand(request: &PullRequest) -> String {
    format!(
        "{}/{}/",
        AgentPaths::ACCOUNT_HOME_ROOT,
        request.account.as_str()
    )
}

#[cfg(test)]
#[path = "../tests/transfer/rsync_argv_tests.rs"]
mod tests;
