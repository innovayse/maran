//! Turning the wire's archive format into the one the operations know, or refusing it.

use maran_ops::files::model::archive_format::ArchiveFormat;

use crate::proto::{AgentError, ArchiveFormat as WireArchiveFormat};
use crate::services::wire::invalid_input::invalid_input;

/// Reads the format number a request carries.
///
/// The unspecified value is refused instead of being read as a default: a
/// request that does not say what it is asking for is not asking for zip. A
/// number this agent's contract does not have is refused for the same reason,
/// and reaches no parser.
///
/// # Errors
///
/// Returns the wire error for the unspecified value and for an unknown number.
pub fn validated_archive_format(format: i32) -> Result<ArchiveFormat, AgentError> {
    match WireArchiveFormat::try_from(format) {
        Ok(WireArchiveFormat::Zip) => Ok(ArchiveFormat::Zip),
        Ok(WireArchiveFormat::TarGz) => Ok(ArchiveFormat::TarGz),
        Ok(WireArchiveFormat::Unspecified) | Err(_) => Err(invalid_input(
            "the archive format is not one this agent reads or writes".to_owned(),
        )),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/validated_archive_format_tests.rs"]
mod tests;
