#![warn(missing_docs)]
// The compiler, not a grep, is the gate: `unsafe` exists in this workspace only
// in maran-agent-core::privs (rules/rust.md "unsafe"). `forbid` cannot be lowered
// by an `#[allow]` further down, so adding unsafe here does not compile at all.
#![forbid(unsafe_code)]
//! maran-distro — the only crate that may know a distribution's name.
//!
//! `detection/` answers what the host is; `adapter` turns that answer into
//! behaviour behind the [`DistroAdapter`] trait, with one implementation folder
//! per family, so every other crate stays free of `if debian` branches
//! (rules/architecture.md).

pub mod adapter;
pub mod adapter_for;
pub mod debian;
pub mod detection;
pub mod family;
pub mod rhel;

pub use adapter::DistroAdapter;
pub use adapter_for::adapter_for;
pub use detection::detect::detect;
pub use detection::detect_error::DetectError;
pub use detection::distro_info::DistroInfo;
pub use detection::os_release::parse;
pub use family::DistroFamily;
