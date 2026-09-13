//! MySQL/MariaDB databases and the dedicated user each one is created with.
//!
//! Three things shape everything in this area.
//!
//! **The agent holds no database credential.** It runs as root and connects over
//! the server's local socket, where the `unix_socket` plugin authenticates
//! `root@localhost` by the connecting process's uid rather than by a password.
//! That is a design decision, not a convenience: a password the agent stores is
//! a password that can be stolen from the agent, and there is no way to store
//! one safely on a host whose whole point is that the agent can already do
//! anything as root. Ensuring the plugin is enabled — and failing the install
//! loudly when it is not — is the installer's job, and this crate assumes it.
//!
//! **Statements are built by interpolating validated types, and that is
//! correct.** The server's DDL cannot parameterise an identifier or the literal
//! in `IDENTIFIED BY`, so there is no placeholder to bind. What makes the
//! interpolation safe is that a
//! [`DatabaseName`](maran_agent_core::validation::db::database_name::DatabaseName),
//! a [`DbUserName`](maran_agent_core::validation::db::db_user_name::DbUserName)
//! and a [`Password`](maran_agent_core::validation::secrets::password::Password)
//! cannot hold a quote, a backtick, a backslash, a semicolon, a space or a
//! newline — the values are validated, not escaped. Every operation here takes
//! those types and never a `&str`, so the guarantee is the signature's rather
//! than each caller's.
//!
//! **The listing is diagnostic, and it is not the tenant boundary.** The server
//! has no notion of a tenant; a name only looks like it belongs to an account
//! because of the prefix this panel puts there. Authorisation lives in the
//! panel's own rows. `list_databases` still refuses to alias one account onto
//! another — it decodes each name at its LAST separator and matches the whole
//! account, never a prefix — because `alice_bob_shop` starts with `alice_` and
//! belongs to `alice_bob`.
//!
//! **Nothing here issues `FLUSH PRIVILEGES`, and that is deliberate.** That
//! statement makes the server re-read the grant tables after somebody has
//! modified them DIRECTLY, with `INSERT`, `UPDATE` or `DELETE` against
//! `mysql.user` and its neighbours. This area never does that: it uses
//! `CREATE USER`, `ALTER USER`, `GRANT` and `DROP USER`, which are
//! account-management statements that take effect immediately on every server
//! in the support matrix. A flush after one of them is a no-op inherited from
//! pre-5.7 habit.
//!
//! It is written down because it was once here and was removed: it survived a
//! mutation run against a real MariaDB — the whole polygon passed without it —
//! and was held up only by unit tests asserting that we sent it. The danger was
//! never the statement, it was the belief that a credential change depended on
//! it. The fake host now REFUSES the statement rather than accepting it, so
//! re-adding one fails loudly instead of passing quietly.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`DbHost`]), one file that really spawns the client ([`ProcessDbHost`]), one
//! error enum ([`DbError`]) that structurally cannot carry the client's output,
//! and `model/` for the typed inputs and outputs.

mod create_database;
mod database_size;
mod db_error;
mod db_host;
mod drop_account_databases;
mod drop_database;
#[cfg(test)]
#[path = "../tests/db/fake_db_host.rs"]
pub(crate) mod fake_db_host;
mod list_databases;
pub mod model;
mod process_db_host;
mod set_database_password;

pub use create_database::create_database;
pub use database_size::database_size;
pub use db_error::DbError;
pub use db_host::DbHost;
pub use drop_account_databases::drop_account_databases;
pub use drop_database::drop_database;
pub use list_databases::list_databases;
pub use model::create_database_request::CreateDatabaseRequest;
pub use model::database_size_report::DatabaseSizeReport;
pub use model::database_summary::DatabaseSummary;
pub use process_db_host::ProcessDbHost;
pub use set_database_password::set_database_password;
