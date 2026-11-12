//! Rendering the whole mail configuration into one stage per daemon.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::{DistroAdapter, DovecotMajor};
use maran_templates::dovecot::dovecot_config_v23::DovecotConfigV23;
use maran_templates::dovecot::dovecot_config_v24::DovecotConfigV24;
use maran_templates::key_value::key_value_server_config::KeyValueServerConfig;
use maran_templates::postfix::postfix_main_config::PostfixMainConfig;
use maran_templates::postfix::postfix_master_config::PostfixMasterConfig;
use maran_templates::rspamd::rspamd_actions::RspamdActions;
use maran_templates::rspamd::rspamd_conf_local::RspamdConfLocal;
use maran_templates::rspamd::rspamd_dkim_signing::RspamdDkimSigning;
use maran_templates::rspamd::rspamd_local_submit::RspamdLocalSubmit;
use maran_templates::rspamd::rspamd_redis::RspamdRedis;
use maran_templates::rspamd::rspamd_worker_controller::RspamdWorkerController;
use maran_templates::rspamd::rspamd_worker_normal::RspamdWorkerNormal;
use maran_templates::rspamd::rspamd_worker_proxy::RspamdWorkerProxy;

use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_settings::MailSettings;
use crate::mail::model::mail_sockets::MailSockets;
use crate::mail::model::mail_stage::MailStage;
use crate::mail::model::mail_unit::MailUnit;
use crate::mail::model::staged_file::StagedFile;
use crate::mail::render_self_clients::render_self_clients;
use crate::ssl::CertificateState;

/// The mode every rendered mail configuration file lands at.
///
/// World-readable because the daemons that read them run as their own users
/// (`postfix`, `redis`, `_rspamd`) and the files hold paths and numbers, never a
/// secret. What must stay private — password hashes, signing keys — lives in
/// directories that are not world-traversable, not in a file of a tighter mode.
pub(crate) const CONFIG_MODE: u32 = 0o644;

/// Renders every file of the mail stack and groups them by the daemon that reads
/// them, in start order.
///
/// Nothing is written here: the result is a plan, and the config-write protocol
/// is what puts it on disk. Platform facts (the directories, the binaries, the
/// units, the map type, the users) come from `distro`; the agent's own decisions
/// (the maps, the sockets) from [`AgentPaths`]; the certificate paths from
/// `certificate`, the store's own answer. `postfix_uid` is the host's Postfix
/// user (see `postfix_uid_from_passwd`): the local-submission rule leaves its
/// mail and root's alone. `host_addresses` are the addresses the host's interfaces
/// carry; they become the table of clients the port 25 service refuses, and it is
/// a file of the Postfix stage so a changed address list is a changed stage.
///
/// # Errors
///
/// Returns [`MailError::Render`] when a template and its render type have
/// drifted apart.
pub fn build_mail_stages(
    distro: &dyn DistroAdapter,
    layout: &MailLayout,
    settings: &MailSettings,
    certificate: &CertificateState,
    postfix_uid: u32,
    host_addresses: &HostAddresses,
) -> Result<Vec<MailStage>, MailError> {
    let manager = distro.service_manager();
    let reload_or_restart = |unit: &str| MailUnit::new(unit).reload_or_restart().map(str::to_owned);
    let is_active = |unit: &str| MailUnit::new(unit).is_active().map(str::to_owned);

    let key_value_unit = layout.key_value_server.service;
    let key_value = MailStage {
        daemon: MailDaemon::KeyValueServer,
        unit: key_value_unit.to_owned(),
        files: vec![file(
            Path::new(layout.key_value_server.config_file),
            KeyValueServerConfig {
                socket_path: layout.key_value_socket(),
            }
            .render_config()
            .map_err(MailError::from)?,
        )],
        // The server has no check that reads a file, so the service manager
        // restarting it onto the new file is the check, and the question of
        // whether it is still up the step after.
        validator_program: manager.clone(),
        validator_arguments: reload_or_restart(key_value_unit).to_vec(),
        validator_class: CallClass::Reload,
        reload_arguments: is_active(key_value_unit).to_vec(),
    };

    let rspamd_directory = Path::new(distro.rspamd_config_directory());
    let rspamd_unit = distro.rspamd_service();
    let rspamd = MailStage {
        daemon: MailDaemon::Rspamd,
        unit: rspamd_unit.to_owned(),
        files: vec![
            file(
                &rspamd_directory.join("local.d/redis.conf"),
                RspamdRedis {
                    socket_path: layout.key_value_socket(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &rspamd_directory.join("local.d/actions.conf"),
                RspamdActions {
                    greylist: settings.spam.greylist(),
                    add_header: settings.spam.add_header(),
                    reject: settings.spam.reject(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &rspamd_directory.join("local.d/dkim_signing.conf"),
                RspamdDkimSigning {
                    selectors_path: AgentPaths::MAIL_DKIM_SELECTORS_PATH.to_owned(),
                    keys_directory: AgentPaths::MAIL_DKIM_DIRECTORY.to_owned(),
                    condition_path: rspamd_directory
                        .join("lua.local.d/maran_dkim_condition.lua")
                        .to_string_lossy()
                        .into_owned(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &rspamd_directory.join("local.d/maran_local_submit.conf"),
                RspamdLocalSubmit {
                    maps_directory: AgentPaths::MAIL_CONFIG_DIRECTORY.to_owned(),
                    max_recipients: MailSettings::RECIPIENT_LIMIT,
                    exempt_uids: vec![0, postfix_uid],
                    host_mail_name: settings.hostname.as_str().to_owned(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            // The rule is a Rspamd MODULE (the file name is the module name), so it lives in
            // plugins.d, and its settings are included from rspamd.conf.local: Rspamd reads
            // neither lua.local.d nor a local.d file of a module it does not ship.
            file(
                &rspamd_directory.join("plugins.d/maran_local_submit.lua"),
                RspamdLocalSubmit::lua_rule().to_owned(),
            ),
            file(
                &rspamd_directory.join("rspamd.conf.local"),
                RspamdConfLocal.render_config().map_err(MailError::from)?,
            ),
            // NOT in plugins.d: the condition is run by the signing module through
            // sign_condition, and Rspamd would try to load it as a module of its own.
            file(
                &rspamd_directory.join("lua.local.d/maran_dkim_condition.lua"),
                RspamdDkimSigning::lua_condition().to_owned(),
            ),
            file(
                &rspamd_directory.join("override.d/worker-proxy.inc"),
                RspamdWorkerProxy {
                    socket_path: MailSockets::MILTER.to_owned(),
                    postfix_group: distro.postfix_user().to_owned(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &rspamd_directory.join("override.d/worker-controller.inc"),
                RspamdWorkerController {
                    socket_path: MailSockets::CONTROLLER.to_owned(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &rspamd_directory.join("override.d/worker-normal.inc"),
                RspamdWorkerNormal
                    .render_config()
                    .map_err(MailError::from)?,
            ),
        ],
        validator_program: distro.rspamadm_binary(),
        validator_arguments: vec!["configtest".to_owned()],
        // The configuration test compiles the rule set, which takes seconds that
        // the short validator class does not allow for.
        validator_class: CallClass::Reload,
        reload_arguments: reload_or_restart(rspamd_unit).to_vec(),
    };

    let dovecot_unit = distro.dovecot_service();
    let dovecot_config = match layout.dovecot_major {
        DovecotMajor::V23 => DovecotConfigV23 {
            pop3_enabled: settings.pop3_enabled,
            certificate_path: certificate.certificate_path.clone(),
            private_key_path: certificate.private_key_path.clone(),
            users_path: AgentPaths::MAIL_DOVECOT_USERS_PATH.to_owned(),
            sasl_socket: MailSockets::SASL.to_owned(),
            lmtp_socket: MailSockets::LMTP.to_owned(),
            postfix_user: distro.postfix_user().to_owned(),
        }
        .render_config(),
        DovecotMajor::V24 => DovecotConfigV24 {
            pop3_enabled: settings.pop3_enabled,
            certificate_path: certificate.certificate_path.clone(),
            private_key_path: certificate.private_key_path.clone(),
            users_path: AgentPaths::MAIL_DOVECOT_USERS_PATH.to_owned(),
            sasl_socket: MailSockets::SASL.to_owned(),
            lmtp_socket: MailSockets::LMTP.to_owned(),
            postfix_user: distro.postfix_user().to_owned(),
        }
        .render_config(),
    }
    .map_err(MailError::from)?;
    let dovecot = MailStage {
        daemon: MailDaemon::Dovecot,
        unit: dovecot_unit.to_owned(),
        files: vec![file(
            &Path::new(distro.dovecot_config_directory()).join("dovecot.conf"),
            dovecot_config,
        )],
        validator_program: distro.doveconf_binary(),
        validator_arguments: vec!["-n".to_owned()],
        validator_class: CallClass::Validate,
        reload_arguments: reload_or_restart(dovecot_unit).to_vec(),
    };

    let postfix_directory = Path::new(distro.postfix_config_directory());
    let postfix_unit = distro.postfix_service();
    let postfix = MailStage {
        daemon: MailDaemon::Postfix,
        unit: postfix_unit.to_owned(),
        files: vec![
            file(
                &postfix_directory.join("main.cf"),
                PostfixMainConfig {
                    hostname: settings.hostname.as_str().to_owned(),
                    message_size_bytes: settings.message_size_bytes(),
                    recipient_limit: MailSettings::RECIPIENT_LIMIT,
                    alias_expansion_limit: MailSettings::ALIAS_EXPANSION_LIMIT,
                    map_type: distro.postfix_map_type().to_owned(),
                    virtual_domains_path: AgentPaths::MAIL_VIRTUAL_DOMAINS_PATH.to_owned(),
                    virtual_mailboxes_path: AgentPaths::MAIL_VIRTUAL_MAILBOXES_PATH.to_owned(),
                    virtual_aliases_path: AgentPaths::MAIL_VIRTUAL_ALIASES_PATH.to_owned(),
                    sender_login_maps_path: AgentPaths::MAIL_SENDER_LOGIN_MAPS_PATH.to_owned(),
                    local_submit_users_path: local_submit_users_path(distro),
                    lmtp_socket: MailSockets::LMTP.to_owned(),
                    sasl_socket: MailSockets::SASL.to_owned(),
                    milter_socket: MailSockets::MILTER.to_owned(),
                    certificate_path: certificate.certificate_path.clone(),
                    private_key_path: certificate.private_key_path.clone(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                &postfix_directory.join("master.cf"),
                PostfixMasterConfig {
                    self_clients_path: AgentPaths::MAIL_SELF_CLIENTS_PATH.to_owned(),
                }
                .render_config()
                .map_err(MailError::from)?,
            ),
            file(
                Path::new(AgentPaths::MAIL_SELF_CLIENTS_PATH),
                render_self_clients(host_addresses)?,
            ),
        ],
        validator_program: distro.postfix_binary(),
        validator_arguments: vec!["check".to_owned()],
        validator_class: CallClass::Validate,
        reload_arguments: reload_or_restart(postfix_unit).to_vec(),
    };

    Ok(vec![key_value, rspamd, dovecot, postfix])
}

/// The table of users allowed to submit mail locally, as the string Postfix is told.
///
/// Built here and in `seed_mail_maps` from the one `AgentPaths`
/// function, so the table Postfix reads is the table the agent seeds.
fn local_submit_users_path(distro: &dyn DistroAdapter) -> String {
    AgentPaths::mail_local_submit_users_path(distro.postfix_config_directory())
        .to_string_lossy()
        .into_owned()
}

/// One rendered file at the mode every mail configuration file has.
fn file(target: &Path, contents: String) -> StagedFile {
    StagedFile {
        target: PathBuf::from(target),
        contents,
        mode: CONFIG_MODE,
    }
}

#[cfg(test)]
#[path = "../tests/mail/build_mail_stages_tests.rs"]
mod tests;
