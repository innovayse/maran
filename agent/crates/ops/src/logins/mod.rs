//! What every file-transfer login of an account has in common, whatever daemon
//! serves it.
//!
//! An account can hold SFTP logins, FTPS logins, or both, and the two areas
//! that create them (`sftp/`, `ftps/`) are deliberately separate: their jails
//! have different lifetimes and tearing one down must never unmount the other.
//! What they genuinely share lands here.
//!
//! Two things do, and they are here for two different reasons.
//!
//! `systemd_escape` is here by the rule of
//! two (rules/rust.md): the second caller is the moment a block moves to a
//! named home, and this one is worse than most to copy, because two copies
//! drifting apart produce a unit systemd refuses to load and a login that lands
//! in an empty directory — on a host, never in a build.
//!
//! **The enumeration is here because it being a lodger in one protocol's area
//! was a security defect.** Asking "which credentials into this account's home
//! exist" from inside `sftp/` gave an answer selected by the SFTP jail, so an
//! FTPS login of the same account was invisible to it — and therefore to the
//! suspension that calls it. The day the first FTPS login existed, a suspended
//! customer would have kept a working write credential while the panel's
//! attestation said every login was locked. A facility more than one area needs
//! is its own area (rules/architecture.md), and here the alternative is not
//! untidiness but a suspension that does not suspend.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`LoginsHost`]), one file that really touches the machine
//! ([`ProcessLoginsHost`]), one error enum ([`LoginsError`]) that structurally
//! cannot carry a tool's output, and `model/` for what the enumeration returns.
//!
//! **The cull of an account's open sessions is here for the same reason as the
//! enumeration**, and it is the third thing the two protocols share. Suspension
//! is checked at authentication only, so neither daemon re-authorises a session
//! it has already admitted; ending those sessions is one question about one uid
//! whichever daemon is serving them, and asking it from inside one protocol's
//! area would have produced an answer that covered that protocol's sessions and
//! silently left the other's running. It is crate-private and is driven by the
//! lock operation, because "end these sessions" is not an rpc this agent offers:
//! it is what suspending an account means.
//!
//! It introduces **no lock of its own**: the write takes the hosting account's
//! existing lock, which never waits, so the set in rules/rust.md "What this
//! agent serialises" is unchanged and so is its wait-for graph.

mod account_logins;
mod account_passwd_row;
mod end_account_sessions;
#[cfg(test)]
#[path = "../tests/logins/fake_logins_host.rs"]
pub(crate) mod fake_logins_host;
mod logins_error;
mod logins_host;
pub mod model;
mod process_logins_host;
mod set_account_logins_locked;
pub(crate) mod systemd_escape;

pub use account_logins::account_logins;
pub use logins_error::LoginsError;
pub use logins_host::LoginsHost;
pub use model::account_lock_outcome::AccountLockOutcome;
pub use model::account_login::AccountLogin;
pub use model::account_login_set::AccountLoginSet;
pub use model::login_protocol::LoginProtocol;
pub use process_logins_host::ProcessLoginsHost;
pub use set_account_logins_locked::set_account_logins_locked;
