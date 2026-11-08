//! Creating the lookup tables mail needs before the first daemon starts.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::safe_write::model::Validator;
use crate::safe_write::model::config_file::ConfigFile;

/// The mode a seeded table lands at. Postfix reads its maps as its own user, and
/// the two files that hold anything private sit in directories that are not
/// world-traversable.
const TABLE_MODE: u32 = 0o644;

/// Creates every lookup table that does not exist yet, empty.
///
/// A mail stack with no domain must still be a valid stack: Postfix refuses to
/// start against a map file it cannot open, and an indexed map needs its index
/// built. So a host enabled before any customer has a domain starts with empty
/// tables, and the data plane replaces them whole as domains arrive.
///
/// **A table that already exists is never touched.** The tables are derivable
/// from the panel's database and are re-pushed by it, but an enable is not the
/// operation that re-pushes them: rewriting a populated table to empty here would
/// drop a customer's mail until the next push.
///
/// Each seeded file goes through the config-write protocol. Only the Postfix
/// tables have a validator that reads the file: `postmap` builds the index of the
/// table (so a table the daemon cannot index is refused here and not at the first
/// message). The passwd-file and the selector map are not parsed by their
/// validators (`doveconf -n` and `rspamadm configtest` read the daemons' own
/// configuration, which does not include an empty data file), so for those two the
/// protocol's guarantee is the atomic create and the rollback, not a parse.
///
/// # Errors
///
/// Returns [`MailError::ConfigRejected`] when a validator refuses a file (it is
/// removed again), [`MailError::ConfigUnreadable`] when a table exists and cannot
/// be read, and the protocol's other failures as [`MailError::ConfigWrite`],
/// [`MailError::SpawnFailed`] and [`MailError::Bound`].
pub fn seed_mail_maps(host: &dyn MailHost, distro: &dyn DistroAdapter) -> Result<(), MailError> {
    let postmap = distro.postmap_binary();
    let map_type = distro.postfix_map_type();
    for path in [
        AgentPaths::MAIL_VIRTUAL_DOMAINS_PATH,
        AgentPaths::MAIL_VIRTUAL_MAILBOXES_PATH,
        AgentPaths::MAIL_VIRTUAL_ALIASES_PATH,
        AgentPaths::MAIL_SENDER_LOGIN_MAPS_PATH,
    ] {
        let target = Path::new(path);
        if host.read_config(target)?.is_some() {
            continue;
        }
        let index = format!("{map_type}:{path}");
        host.write_file_unreloaded(
            MailDaemon::Postfix,
            distro.postfix_service(),
            &ConfigFile {
                target,
                contents: "",
                mode: TABLE_MODE,
            },
            &Validator {
                program: &postmap,
                arguments: &[index.as_str()],
                class: CallClass::Mutation,
            },
        )?;
    }

    let users = Path::new(AgentPaths::MAIL_DOVECOT_USERS_PATH);
    if host.read_config(users)?.is_none() {
        host.write_file_unreloaded(
            MailDaemon::Dovecot,
            distro.dovecot_service(),
            &ConfigFile {
                target: users,
                contents: "",
                mode: TABLE_MODE,
            },
            &Validator {
                program: &distro.doveconf_binary(),
                arguments: &["-n"],
                class: CallClass::Validate,
            },
        )?;
    }

    let selectors = Path::new(AgentPaths::MAIL_DKIM_SELECTORS_PATH);
    if host.read_config(selectors)?.is_none() {
        host.write_file_unreloaded(
            MailDaemon::Rspamd,
            distro.rspamd_service(),
            &ConfigFile {
                target: selectors,
                contents: "",
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

#[cfg(test)]
#[path = "../tests/mail/seed_mail_maps_tests.rs"]
mod tests;
