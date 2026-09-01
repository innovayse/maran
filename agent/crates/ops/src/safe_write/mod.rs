//! The one path a system configuration file may take: write beside the
//! target, `fsync`, atomically swap in, validate, reload — and on a failure
//! at validation or reload, restore the previous content
//! (rules/rust.md "Config writes: render → swap → validate").
//!
//! Every site, pool and certificate write in later plan tasks calls
//! [`render_validate_swap::write_config`] and never touches `std::fs`
//! directly: partial writes are forbidden, and an area that needs a
//! variation on this protocol extends it rather than writing its own copy.
//!
//! [`write_config_set::write_config_set`] is that extension, and the reason it
//! exists is a certificate renewal: a key and a certificate must land as ONE
//! change, because between two separate writes they are a mismatched pair and
//! `nginx -t` — which really does load them — refuses. So the set is renamed
//! into place first, and only then validated and reloaded, once.

mod config_host;
pub mod model;
mod remove_config;
mod render_validate_swap;
mod rollback_guard;
mod safe_write_error;
mod write_config_set;

pub use config_host::ConfigHost;
pub use maran_agent_core::command_outcome::CommandOutcome;
pub use remove_config::remove_config;
pub use render_validate_swap::write_config;
pub use rollback_guard::RollbackGuard;
pub use safe_write_error::SafeWriteError;
pub use write_config_set::write_config_set;
