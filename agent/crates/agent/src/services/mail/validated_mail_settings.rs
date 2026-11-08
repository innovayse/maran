//! Revalidating what an `EnableMail` request carries.

use maran_agent_core::validation::mail::mail_message_size_mib::MailMessageSizeMib;
use maran_agent_core::validation::mail::mail_server_hostname::MailServerHostname;
use maran_agent_core::validation::mail::spam_thresholds::SpamThresholds;
use maran_ops::mail::model::mail_settings::MailSettings;

use crate::proto::{AgentError, EnableMailRequest, ErrorCode};
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the settings an `EnableMail` request asks for through the validated
/// types, so nothing reaches a rendered configuration file that did not pass the
/// agent's own check.
///
/// The panel validated all of it already. This is the agent's own gate, and it
/// decides lines of four daemons' configuration, which is why it is unconditional:
/// the hostname is a mail name (and nothing a newline can ride in), the message
/// size is nonzero and bounded, and the three spam scores are in range and
/// strictly ascending.
///
/// Three fields of the contract name behaviour this build's host lifecycle cannot
/// yet carry out: the virus scanner, forwarding to other hosts, and the webmail
/// master login. They are REFUSED with [`ErrorCode::NotImplemented`] when asked
/// for and never quietly dropped, because a stack that came up without something
/// the panel believes it enabled is a worse state than a refusal. The absent value
/// of each (false, false, empty) is what a panel that does not use them sends, and
/// is accepted.
///
/// # Errors
///
/// Returns the wire error for a hostname, a message size or a set of spam scores
/// the agent will not accept, and a `NotImplemented` error naming the first
/// unsupported option the request switches on.
pub fn validated_mail_settings(request: &EnableMailRequest) -> Result<MailSettings, AgentError> {
    let hostname = MailServerHostname::parse(&request.hostname)
        .map_err(|error| invalid_input(format!("hostname: {error}")))?;
    let message_size = MailMessageSizeMib::parse(request.message_size_mib)
        .map_err(|error| invalid_input(format!("message size: {error}")))?;
    let spam = SpamThresholds::parse(
        request.spam_greylist_score,
        request.spam_add_header_score,
        request.spam_reject_score,
    )
    .map_err(|error| invalid_input(format!("spam scores: {error}")))?;

    if request.antivirus_enabled {
        return Err(not_available("the virus scanner"));
    }
    if request.allow_external_forwarding {
        return Err(not_available("forwarding to other hosts"));
    }
    if !request.webmail_master_hash.is_empty() {
        return Err(not_available("the webmail master login"));
    }

    Ok(MailSettings {
        hostname,
        pop3_enabled: request.pop3_enabled,
        message_size,
        spam,
    })
}

/// The refusal for an option this build cannot carry out. The request value is
/// never quoted: one of the three is a credential.
fn not_available(what: &str) -> AgentError {
    AgentError {
        code: ErrorCode::NotImplemented as i32,
        message: format!("{what} is not available in this agent build"),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/mail/validated_mail_settings_tests.rs"]
mod tests;
