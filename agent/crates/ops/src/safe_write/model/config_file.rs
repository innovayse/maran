//! One file of a configuration change, and the mode it must land at.

use std::path::Path;

/// A single target of [`super::super::write_config_set::write_config_set`]: what
/// to write, where, and at which mode.
///
/// The mode is part of the value rather than something a caller applies
/// afterwards, because for one of these files it is the entire protection: a
/// private key is `0600` from the moment it exists or it has already leaked. The
/// protocol sets it on the temporary file BEFORE the rename, so the name never
/// points at a file that was briefly wider.
pub struct ConfigFile<'a> {
    /// Absolute path the content is renamed onto.
    pub target: &'a Path,
    /// The rendered content, exactly as it should land.
    pub contents: &'a str,
    /// The mode the file must have, as an octal `0o600`-style constant.
    pub mode: u32,
}
