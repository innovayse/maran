//! The readings beside a mail state: versions, the password scheme, the queue, Sieve.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_details::MailDetails;
use crate::mail::model::mail_state::MailState;
use crate::mail::model::observed_mail::ObservedMail;
use crate::mail::model::tool_version::ToolVersion;

/// What `doveadm` prints in its usage text when the Sieve plugin is loaded: the
/// plugin registers `sieve list`, `sieve put` and the like.
const SIEVE_COMMAND_MARKER: &str = "sieve list";

/// What `doveadm` prints at the top of its usage text. A usage text without the
/// Sieve marker is the host saying no; any other output says nothing.
const USAGE_MARKER: &str = "usage:";

/// Reads, from the host, what the status reports beyond `state`.
///
/// Every reading is made by asking a daemon's own tool, and only of a daemon that
/// is installed (and, for the queue, running): a version comes from the program
/// that would run, the password scheme from the IMAP server's own list of what it
/// verifies, the queue from the MTA's own listing. A tool that cannot be started,
/// outruns its deadline or prints something this does not understand leaves its
/// field `None` — the status then says it did not observe, which is the only
/// honest word for a reading that failed (rules/testing.md, "A check must be able
/// to observe what it reports on"). Nothing here fails the call: a half-installed
/// stack is a state worth reporting, not a reason to withhold the state.
///
/// UNVERIFIED on a real host: the output shapes of `doveadm --version`,
/// `rspamadm --version` and the Sieve marker in `doveadm`'s usage text are read
/// from documentation, not from a booted Dovecot or Rspamd. The parsers refuse
/// what they do not recognise, so a wrong guess reads as "not observed" and never
/// as a wrong value; the lifecycle suite owes the measurement.
///
/// Read-only and idempotent. Tools run in the `Query` class.
#[must_use]
pub fn observe_mail(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    state: MailState,
) -> ObservedMail {
    let installed = |daemon| {
        state
            .daemons
            .iter()
            .any(|each| each.daemon == daemon && each.installed)
    };
    let running = |daemon| {
        state
            .daemons
            .iter()
            .any(|each| each.daemon == daemon && each.active)
    };

    let postfix_version = installed(MailDaemon::Postfix)
        .then(|| ask(host, &distro.postconf_binary(), &["-h", "mail_version"]))
        .flatten()
        .and_then(|outcome| ToolVersion::parse_output(&outcome.stdout));
    let rspamd_version = installed(MailDaemon::Rspamd)
        .then(|| ask(host, &distro.rspamadm_binary(), &["--version"]))
        .flatten()
        .and_then(|outcome| ToolVersion::parse_output(&outcome.stdout));
    let dovecot_version = installed(MailDaemon::Dovecot)
        .then(|| ask(host, &distro.doveadm_binary(), &["--version"]))
        .flatten()
        .and_then(|outcome| ToolVersion::parse_output(&outcome.stdout));

    let scheme = distro.mail_password_scheme().dovecot_name();
    let password_scheme_listed = installed(MailDaemon::Dovecot)
        .then(|| ask(host, &distro.doveadm_binary(), &["pw", "-l"]))
        .flatten()
        .map(|outcome| scheme_is_listed(&outcome.stdout, scheme));

    let queue_messages = running(MailDaemon::Postfix)
        .then(|| ask(host, &distro.postqueue_binary(), &["-j"]))
        .flatten()
        .map(|outcome| count_queued_messages(&outcome.stdout));

    let sieve_available = installed(MailDaemon::Dovecot)
        .then(|| run(host, &distro.doveadm_binary(), &["sieve"]))
        .flatten()
        .and_then(|outcome| sieve_in_usage(&outcome));

    ObservedMail {
        state,
        details: MailDetails {
            postfix_version,
            dovecot_version,
            rspamd_version,
            password_scheme_listed,
            queue_messages,
            sieve_available,
        },
    }
}

/// Runs `program` and answers its outcome when it exited zero, else `None`.
fn ask(host: &dyn MailHost, program: &AdapterBinary, arguments: &[&str]) -> Option<CommandOutcome> {
    run(host, program, arguments).filter(|outcome| outcome.status == 0)
}

/// Runs `program` and answers its outcome whatever its exit status, or `None`
/// when it could not be started or outran its bound. The failure is logged once,
/// here, at the boundary that absorbs it.
fn run(host: &dyn MailHost, program: &AdapterBinary, arguments: &[&str]) -> Option<CommandOutcome> {
    match host.run(program, arguments, CallClass::Query) {
        Ok(outcome) => Some(outcome),
        Err(error) => {
            tracing::warn!(%error, "a mail status reading could not be made");
            None
        }
    }
}

/// Whether `scheme` is one word of the IMAP server's list of verifiable schemes,
/// compared without regard to case. A whole word and not a substring: `CRYPT` is
/// not `BLF-CRYPT`.
fn scheme_is_listed(listing: &str, scheme: &str) -> bool {
    listing
        .split_whitespace()
        .any(|word| word.eq_ignore_ascii_case(scheme))
}

/// How many messages a `postqueue -j` listing holds: one JSON object per line.
fn count_queued_messages(listing: &str) -> u32 {
    let lines = listing
        .lines()
        .filter(|line| !line.trim().is_empty())
        .count();
    u32::try_from(lines).unwrap_or(u32::MAX)
}

/// Whether `doveadm`'s usage text lists the Sieve commands: yes when it does, no
/// when it printed a usage text without them, and nothing known otherwise.
fn sieve_in_usage(outcome: &CommandOutcome) -> Option<bool> {
    let text = format!("{}\n{}", outcome.stdout, outcome.stderr).to_ascii_lowercase();
    if text.contains(SIEVE_COMMAND_MARKER) {
        Some(true)
    } else if text.contains(USAGE_MARKER) {
        Some(false)
    } else {
        None
    }
}

#[cfg(test)]
#[path = "../tests/mail/observe_mail_tests.rs"]
mod tests;
