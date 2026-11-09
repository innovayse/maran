//! The container formats an archive operation reads and writes.

/// A closed set, never a caller-supplied command line (files.proto).
///
/// The format is stated by the caller AND checked against the file's own magic
/// bytes on extraction, so a zip presented as `tar.gz` is refused instead of
/// being handed to the wrong parser.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ArchiveFormat {
    /// A zip archive; members stored or deflated.
    Zip,
    /// A tar archive compressed as one gzip stream.
    TarGz,
}
