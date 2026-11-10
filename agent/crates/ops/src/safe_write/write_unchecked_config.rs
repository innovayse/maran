//! The config-write protocol for a file no tool can check.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::write_config_set::swap_set;
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError};

/// A host that runs nothing, because this protocol never asks it to.
///
/// The shared body takes a host only to run a validator or a reload; with neither
/// there is no question to put to it. Answering "could not start" rather than a
/// success means that a future edit which did make the body run something here
/// fails loudly instead of being told a program passed.
struct NothingToRun;

impl ConfigHost for NothingToRun {
    /// Refuses every program: this protocol has none.
    ///
    /// # Errors
    ///
    /// Always returns [`SafeWriteError::SpawnFailed`].
    fn run(
        &self,
        program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        Err(SafeWriteError::SpawnFailed {
            program: program.as_str().to_owned(),
            reason: "this write has no validator and no reload".to_owned(),
        })
    }
}

/// Writes `file` through the protocol — temporary file at its final mode beside
/// the target, `fsync` of the file and its directory, atomic rename — and stops
/// there: no validator and no reload.
///
/// For a file whose bytes a validated type already proves and that no program can
/// check: a DNSSEC signing key (issue #452), written from a `DnssecKeyMaterial`
/// whose lengths, alphabet and key tag were verified at construction, and that the
/// daemon reads only when it is told to load a zone. A validator passed here to
/// make a slot non-empty would be decoration (rules/testing.md "A check must be
/// able to observe what it reports on"), so this entry says instead that there is
/// none. It shares [`super::write_config_set::write_config_set`]'s body, so the
/// temporary-file, mode, `fsync` and rename steps cannot drift from the checked
/// writers', and it runs under the same host-wide lock.
///
/// The mode is set on the temporary file BEFORE the rename, so the name never
/// points at a file that was briefly wider than `file.mode`.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] or [`SafeWriteError::Sync`] when the
/// temporary file cannot be written, have its mode set or be flushed — the target
/// is untouched — and [`SafeWriteError::Rename`] when the swap fails.
pub fn write_unchecked_config(file: &ConfigFile<'_>) -> Result<(), SafeWriteError> {
    swap_set(&NothingToRun, std::slice::from_ref(file), None, None)
}

#[cfg(test)]
#[path = "../tests/safe_write/write_unchecked_config_tests.rs"]
mod tests;
