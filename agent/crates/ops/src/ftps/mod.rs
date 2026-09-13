//! FTPS logins: system accounts served by this panel's own vsftpd instance,
//! each one chrooted into a root-owned jail that its account's real home is
//! bind-mounted into.
//!
//! The daemon half of the area is [`enable_ftps`], [`disable_ftps`],
//! [`get_ftps_status`] and [`reload_ftps_tls`]; the login half is
//! [`create_ftps_user`], [`set_ftps_password`], [`delete_ftps_user`] and
//! [`remove_account_ftps`], the
//! last of which is the FTPS step of the account-deletion cascade. [`FtpsJail`]
//! carries the paths and the mount unit name every login operation derives its
//! arguments from.
//!
//! Two facts about the jail were measured on real hosts, and both are why this
//! type exists at all rather than a parameter on the SFTP one:
//!
//! **The base is a SIBLING of `/var/lib/maran`, not a child.** `/var/lib/maran`
//! is `maran:maran 0750`, so while a jail base sat under it, an ancestor of
//! every customer's chroot was owned by an unprivileged uid — which is both a
//! rename-it-aside escalation and, for OpenSSH, a refusal of every login on
//! every real install. See
//! [`AgentPaths::FTPS_JAIL_ROOT`](maran_agent_core::agent_paths::AgentPaths::FTPS_JAIL_ROOT).
//!
//! **vsftpd `chdir()`s into the home AFTER dropping to the account's uid**,
//! unlike sshd, which chroots as root. Every component of the jail path must
//! therefore be traversable by that uid, which is why the base is `0711` and
//! not `0700`: measured on a polygon host, `0700` answered
//! `500 OOPS: cannot change directory:/var/lib/maran-ftps/<account>` and, under
//! forced TLS, broke the connection outright, because the daemon writes that
//! line in the clear on a channel the client reads as TLS.

mod create_ftps_user;
mod delete_ftps_user;
mod disable_ftps;
mod enable_ftps;
mod ensure_account_jail;
#[cfg(test)]
#[path = "../tests/ftps/fake_ftps_host.rs"]
pub(crate) mod fake_ftps_host;
mod ftps_error;
mod ftps_host;
mod get_ftps_status;
pub mod model;
mod probe_listen_mode;
mod process_ftps_host;
mod reload_ftps_tls;
mod remove_account_ftps;
mod set_ftps_password;
mod validate_candidate_config;

pub use create_ftps_user::create_ftps_user;
pub use delete_ftps_user::delete_ftps_user;
pub use disable_ftps::disable_ftps;
pub use enable_ftps::enable_ftps;
pub use ftps_error::FtpsError;
pub use ftps_host::FtpsHost;
pub use get_ftps_status::get_ftps_status;
pub use model::candidate_outcome::CandidateOutcome;
pub use model::ftps_configuration::FtpsConfiguration;
pub use model::ftps_jail::FtpsJail;
pub use model::ftps_state::FtpsState;
pub use model::ftps_unit::FtpsUnit;
pub use model::ftps_user_request::FtpsUserRequest;
pub use model::listen_mode::ListenMode;
pub use probe_listen_mode::probe_listen_mode;
pub use process_ftps_host::ProcessFtpsHost;
pub use reload_ftps_tls::reload_ftps_tls;
pub use remove_account_ftps::remove_account_ftps;
pub use set_ftps_password::set_ftps_password;
pub use validate_candidate_config::validate_candidate_config;
