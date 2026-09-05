//! Input validation: the primitives every command handler runs before it touches
//! the system. Validation lives in one crate so that "is this name safe?" and
//! "is this path inside the account's home?" have exactly one answer each,
//! rather than one per call site (rules/security.md: defense in depth).
//!
//! Two shapes live here. Most modules are validated names — a constructor that
//! can fail, and a type whose existence is the proof it succeeded. `secrets::secret`
//! is the other: it validates nothing and only refuses to print itself.
//!
//! Grouped by the domain the value ends up in, so a reader finds a validator by
//! asking "where is this written?": `system/` becomes OS objects, `db/` reaches
//! MySQL/MariaDB, `web/` is written into web-server configuration or matched on
//! by the firewall, `fs/` names and modes filesystem entries, `secrets/` never
//! leaves memory unredacted.
//! Every type keeps its own `*_error.rs` beside it, as everywhere else.
//!
//! `prefixed_name` and `prefix_problem` are the exception to the grouping: they
//! are not a validated value of any domain but the shared construction core the
//! three account-prefixed names — a database, a database user and an SFTP login
//! — are built from, so they sit at this level and stay crate-internal.

pub mod db;
pub mod fs;
pub(crate) mod prefix_problem;
pub(crate) mod prefixed_name;
pub mod secrets;
pub mod system;
pub mod web;
