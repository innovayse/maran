//! Inputs the config-write protocol accepts, one type per file
//! (rules/rust.md "Operation anatomy").

pub mod config_file;
pub mod reload;
pub mod validator;

pub use config_file::ConfigFile;
pub use reload::Reload;
pub use validator::Validator;
