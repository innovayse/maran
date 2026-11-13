//! Running the installer's root package units and reading what the host's package sources
//! offer: the machinery shared by the PHP area and the app store.
//!
//! The package manager is never run by the agent: its own sandbox keeps `/usr` read-only, so
//! it runs in a templated root unit the installer ships (`maran-app-install@<package>`,
//! `maran-app-remove@<package>`; a Node.js module stream on the RHEL family has units of its own,
//! `maran-app-nodejs-install@<major>` and `maran-app-nodejs-remove@<major>`). This area starts one and reads its failure back; WHICH
//! package, and whether the caller may ask for it, is decided by the area that calls it.

mod classify_package_unit_failure;
pub mod install_run;
mod offered_packages;
mod package_error;
mod query_offered_packages;
mod run_nodejs_module_unit;
mod run_package_unit;

pub use offered_packages::OfferedPackages;
pub use package_error::PackageError;
pub use query_offered_packages::query_offered_packages;
pub(crate) use run_nodejs_module_unit::{run_nodejs_install_unit, run_nodejs_remove_unit};
pub(crate) use run_package_unit::{run_package_unit, run_unit_reporting};
