#![warn(missing_docs)]
// The compiler, not a grep, is the gate: `unsafe` exists in this workspace only
// in maran-agent-core::privs (rules/rust.md "unsafe"). `forbid` cannot be lowered
// by an `#[allow]` further down, so adding unsafe here does not compile at all.
#![forbid(unsafe_code)]
//! maran-templates — askama render types for the system configs the
//! agent writes (`templates/{nginx,php-fpm,vsftpd,systemd,nftables}/`).
//! What is rendered here reaches disk through the safe-write protocol: render →
//! temp file → `fsync` → atomic rename → validate → reload (rules/rust.md
//! "Config writes: render → swap → validate"). The validation runs AFTER the
//! rename because the validating tool reads the config tree by path and cannot
//! see a temporary file, so a render is proved by the tool only once it is the
//! real file at the real path — which is also why a kill in that window leaves
//! unvalidated content live. An earlier version of this comment stated the two
//! steps in the opposite order. Byte-exact expected renders live in
//! `tests/golden/`.

pub mod nftables;
pub mod nginx;
pub mod php_fpm;
pub mod render_error;
pub mod systemd;
pub mod vsftpd;
