//! Cross-crate helpers, one file per named purpose.
//!
//! The same shape the SPA uses in `frontend/src/utils/`: a folder of small,
//! single-purpose units — `formatDate.ts`, not `utils.ts`. What is banned is the
//! catch-all (`util.rs`, `helpers.rs`, `misc.rs`), because a folder whose name is
//! "everything else" has no rule for what belongs in it, and within a year it is
//! the largest file nobody dares change.
//!
//! A helper earns a place here when a SECOND crate needs it. Until then it stays
//! private beside its only caller, where it can be changed without a thought for
//! anyone else.

pub mod current_uid;
pub mod directory;
pub mod spawn_argv;
pub mod system_account;
pub mod system_accounts;
