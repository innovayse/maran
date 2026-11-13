//! The mail stack as a host service: Postfix, Dovecot, the Rspamd spam filter and
//! the key-value server it keeps counters in, driven by the service manager.
//!
//! This area is the HOST half of mail. [`enable_mail`], [`disable_mail`],
//! [`get_mail_status`] and [`reload_mail_tls`] bring the four daemons to a rendered
//! configuration, start and stop them, and observe them. What flows through them —
//! domains, mailboxes, aliases, signing keys — is the data plane's and is not here.
//!
//! The daemons are the distribution's packaged units, installed MASKED by
//! `installer/lib/89-mail.sh` so that none can start, and unmasked only by
//! [`enable_mail`]. The configuration is Maran's: each file the packages ship is
//! replaced whole through the config-write protocol, never edited in place.
//!
//! Three facts about the stack shape everything here and were measured from the
//! distributions' own documentation rather than on a host (see
//! `research_notes/mail-stack-inventory.md`): Dovecot's configuration language
//! changed between 2.3 and 2.4 and Debian 13 ships 2.4; the key-value server is
//! Redis on one release and Valkey on the next; and Postfix's Berkeley DB maps are
//! not on every release, so the map type is a fact of the host.

mod build_mail_stages;
mod converge_mail_stage;
mod disable_mail;
mod enable_mail;
#[cfg(test)]
#[path = "../tests/mail/fake_mail_host.rs"]
pub(crate) mod fake_mail_host;
mod get_mail_status;
mod local_submission_guard;
mod local_submission_tables;
mod mail_error;
mod mail_host;
mod mail_lifecycle_lock;
pub mod model;
mod observe_mail;
mod parse_listening_sockets;
mod port_bindings;
mod postfix_uid_from_passwd;
mod process_mail_host;
mod read_mail_status;
mod refresh_self_clients;
mod reload_mail_tls;
mod remove_local_submission;
mod render_self_clients;
mod seed_local_submission_maps;
mod seed_mail_maps;
mod seed_missing_local_submission_maps;
mod set_local_submission;
mod stop_mail_daemons;
mod wait_for_mail_ports;

pub use build_mail_stages::build_mail_stages;
pub use converge_mail_stage::converge_mail_stage;
pub use disable_mail::disable_mail;
pub use enable_mail::enable_mail;
pub use get_mail_status::get_mail_status;
pub use mail_error::MailError;
pub use mail_host::MailHost;
pub use model::daemon_state::DaemonState;
pub use model::listening_socket::ListeningSocket;
pub use model::local_submission_hazard::LocalSubmissionHazard;
pub use model::local_submission_key::LocalSubmissionKey;
pub use model::mail_daemon::MailDaemon;
pub use model::mail_details::MailDetails;
pub use model::mail_layout::MailLayout;
pub use model::mail_ports::MailPorts;
pub use model::mail_settings::MailSettings;
pub use model::mail_sockets::MailSockets;
pub use model::mail_stage::MailStage;
pub use model::mail_state::MailState;
pub use model::mail_unit::MailUnit;
pub use model::observed_mail::ObservedMail;
pub use model::path_facts::PathFacts;
pub use model::port_binding::PortBinding;
pub use model::port_state::PortState;
pub use model::self_clients_outcome::SelfClientsOutcome;
pub use model::staged_file::StagedFile;
pub use model::tool_version::ToolVersion;
pub use observe_mail::observe_mail;
pub use parse_listening_sockets::parse_listening_sockets;
pub use port_bindings::port_bindings;
pub use process_mail_host::ProcessMailHost;
pub use read_mail_status::read_mail_status;
pub use refresh_self_clients::refresh_self_clients;
pub use reload_mail_tls::reload_mail_tls;
pub use remove_local_submission::remove_local_submission;
pub use seed_mail_maps::seed_mail_maps;
pub use seed_missing_local_submission_maps::seed_missing_local_submission_maps;
pub use set_local_submission::set_local_submission;
pub use wait_for_mail_ports::wait_for_mail_ports;
