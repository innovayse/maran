//! Input validation: the primitives every command handler runs before it touches
//! the system. Validation lives in one crate so that "is this name safe?" and
//! "is this path inside the account's home?" have exactly one answer each,
//! rather than one per call site (rules/security.md: defense in depth).

pub mod domain;
pub mod domain_error;
pub mod file_mode;
pub mod file_mode_error;
pub mod name;
pub mod name_error;
pub mod path;
pub mod path_error;
pub mod php_version;
pub mod php_version_error;
pub mod relative_path;
pub mod relative_path_error;
pub mod upstream;
pub mod upstream_error;
