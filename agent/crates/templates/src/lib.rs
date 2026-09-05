#![warn(missing_docs)]
// The compiler, not a grep, is the gate: `unsafe` exists in this workspace only
// in maran-agent-core::privs (rules/rust.md "unsafe"). `forbid` cannot be lowered
// by an `#[allow]` further down, so adding unsafe here does not compile at all.
#![forbid(unsafe_code)]
//! maran-templates — askama render types for the system configs the
//! agent writes (`templates/{nginx,php-fpm,vsftpd,systemd,nftables}/`).
//! Rendering follows the safe-write protocol: render → temp file → validate →
//! atomic rename → reload (rules/rust.md "Validation first"). Byte-exact
//! expected renders live in `tests/golden/`.

pub mod nftables;
pub mod nginx;
pub mod php_fpm;
pub mod render_error;
pub mod systemd;
