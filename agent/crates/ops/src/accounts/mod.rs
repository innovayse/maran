//! Hosting accounts at the operating-system level: the system user that backs an
//! account, its home directory, its suspension state and its disk quota.
//!
//! Every operation is idempotent and re-validates its inputs (rules/rust.md
//! "Validation first"): the API has already checked them, and the agent checks
//! again because it runs as root and the API does not.

mod account_error;
mod account_operations;
mod command_outcome;
pub mod model;
mod process_system_host;
mod system_host;

pub use account_error::AccountError;
pub use account_operations::AccountOperations;
pub use command_outcome::CommandOutcome;
pub use model::account_usage::AccountUsage;
pub use model::created_account::CreatedAccount;
pub use process_system_host::ProcessSystemHost;
pub use system_host::SystemHost;
