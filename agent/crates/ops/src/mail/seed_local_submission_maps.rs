//! Creating the maps the local-submission binding reads.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::local_submission_tables::EMPTY_TABLE;
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::seed_mail_maps::TABLE_MODE;
use crate::safe_write::model::Validator;
use crate::safe_write::model::config_file::ConfigFile;

/// Creates the five Rspamd maps of the local-submission binding empty (issue #518).
/// "Empty" is the comment line [`EMPTY_TABLE`], not zero bytes: Rspamd does not
/// refresh a map from an empty file.
///
/// An empty map is the closed state: the rule finds no account for any uid and
/// refuses the message (an empty fan-out map only means no alias fans out). Existing maps are never touched, for the reason
/// `seed_mail_maps` gives.
pub(crate) fn seed_local_submission_maps(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
) -> Result<(), MailError> {
    for path in [
        AgentPaths::MAIL_LOCAL_SUBMIT_ACCOUNTS_PATH,
        AgentPaths::MAIL_ACCOUNT_SENDER_DOMAINS_PATH,
        AgentPaths::MAIL_ACCOUNT_SEND_LIMITS_PATH,
        AgentPaths::MAIL_MAILBOX_ACCOUNTS_PATH,
        AgentPaths::MAIL_ALIAS_FANOUT_PATH,
    ] {
        let target = Path::new(path);
        if host.read_config(target)?.is_some() {
            continue;
        }
        host.write_file_unreloaded(
            MailDaemon::Rspamd,
            distro.rspamd_service(),
            &ConfigFile {
                target,
                contents: EMPTY_TABLE,
                mode: TABLE_MODE,
            },
            &Validator {
                program: &distro.rspamadm_binary(),
                arguments: &["configtest"],
                class: CallClass::Reload,
            },
        )?;
    }
    Ok(())
}
