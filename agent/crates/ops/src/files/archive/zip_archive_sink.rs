//! Writing a zip archive.

use std::fs::File;
use std::io::{self, Read};

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;
use zip::result::ZipError;
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

use super::archive_refusal::ArchiveRefusal;
use super::archive_sink::ArchiveSink;
use super::marked_reader::MarkedReader;
use super::write_refusal::write_refusal;

/// Mode recorded for a directory.
const DIRECTORY_MODE: u32 = 0o755;

/// Mode recorded for an executable file.
const EXECUTABLE_MODE: u32 = 0o755;

/// Mode recorded for every other file.
const PLAIN_MODE: u32 = 0o644;

/// Sizes from here up are written with zip64 fields.
const ZIP64_THRESHOLD: u64 = 0xFFFF_FFFF;

/// A zip being written into a file the caller made.
///
/// Members are deflated, which every reader understands; the zip crate is built
/// with that method and stored only. Nothing from the source tree's metadata is
/// recorded: no owner, no timestamp, no mode beyond the three above.
pub(crate) struct ZipArchiveSink {
    /// The writer over the destination file.
    writer: ZipWriter<File>,
}

impl ZipArchiveSink {
    /// A sink writing into `file`.
    pub(crate) fn new(file: File) -> Self {
        Self {
            writer: ZipWriter::new(file).set_auto_large_file(),
        }
    }
}

impl ArchiveSink for ZipArchiveSink {
    fn add_directory(&mut self, name: &ArchiveMemberName) -> Result<(), ArchiveRefusal> {
        self.writer
            .add_directory(
                name.as_str(),
                SimpleFileOptions::DEFAULT.unix_permissions(DIRECTORY_MODE),
            )
            .map_err(zip_refusal)
    }

    fn add_file(
        &mut self,
        name: &ArchiveMemberName,
        executable: bool,
        size: u64,
        data: &mut dyn Read,
    ) -> Result<(), ArchiveRefusal> {
        let mode = if executable {
            EXECUTABLE_MODE
        } else {
            PLAIN_MODE
        };
        let options = SimpleFileOptions::DEFAULT
            .compression_method(CompressionMethod::Deflated)
            .unix_permissions(mode)
            .large_file(size >= ZIP64_THRESHOLD);
        self.writer
            .start_file(name.as_str(), options)
            .map_err(zip_refusal)?;

        let mut source = MarkedReader::new(data);
        let copied = io::copy(&mut source, &mut self.writer).map_err(|error| {
            if source.read_failed() {
                ArchiveRefusal::ReadFailed
            } else {
                write_refusal(&error)
            }
        })?;
        if copied != size {
            return Err(ArchiveRefusal::ReadFailed);
        }
        Ok(())
    }

    fn finish(self: Box<Self>) -> Result<File, ArchiveRefusal> {
        self.writer.finish().map_err(zip_refusal)
    }
}

/// The refusal for a failure of the zip writer.
fn zip_refusal(error: ZipError) -> ArchiveRefusal {
    match error {
        ZipError::Io(error) => write_refusal(&error),
        _ => ArchiveRefusal::WriteFailed,
    }
}
