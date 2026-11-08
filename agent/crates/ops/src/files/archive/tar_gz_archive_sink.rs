//! Writing a gzip-compressed tar archive.

use std::fs::File;
use std::io::{self, Read};

use flate2::Compression;
use flate2::write::GzEncoder;
use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;
use tar::{Builder, EntryType, Header};

use super::archive_refusal::ArchiveRefusal;
use super::archive_sink::ArchiveSink;
use super::marked_reader::MarkedReader;
use super::write_refusal::write_refusal;

/// Gzip level: the default trade of the tools customers compare it with.
const GZIP_LEVEL: u32 = 6;

/// Mode recorded for a directory.
const DIRECTORY_MODE: u32 = 0o755;

/// Mode recorded for an executable file.
const EXECUTABLE_MODE: u32 = 0o755;

/// Mode recorded for every other file.
const PLAIN_MODE: u32 = 0o644;

/// A `.tar.gz` being written into a file the caller made.
///
/// Headers are GNU, so names past the classic limit get a long-name record;
/// owner, group and time are left at zero, and the mode is one of three values.
pub(crate) struct TarGzArchiveSink {
    /// The tar builder over the gzip encoder over the destination file.
    builder: Builder<GzEncoder<File>>,
}

impl TarGzArchiveSink {
    /// A sink writing into `file`.
    pub(crate) fn new(file: File) -> Self {
        Self {
            builder: Builder::new(GzEncoder::new(file, Compression::new(GZIP_LEVEL))),
        }
    }
}

impl ArchiveSink for TarGzArchiveSink {
    fn add_directory(&mut self, name: &ArchiveMemberName) -> Result<(), ArchiveRefusal> {
        let mut header = Header::new_gnu();
        header.set_entry_type(EntryType::Directory);
        header.set_size(0);
        header.set_mode(DIRECTORY_MODE);
        self.builder
            .append_data(&mut header, format!("{}/", name.as_str()), io::empty())
            .map_err(|error| write_refusal(&error))
    }

    fn add_file(
        &mut self,
        name: &ArchiveMemberName,
        executable: bool,
        size: u64,
        data: &mut dyn Read,
    ) -> Result<(), ArchiveRefusal> {
        let mut header = Header::new_gnu();
        header.set_entry_type(EntryType::Regular);
        header.set_size(size);
        header.set_mode(if executable {
            EXECUTABLE_MODE
        } else {
            PLAIN_MODE
        });

        let mut source = MarkedReader::new(data);
        self.builder
            .append_data(&mut header, name.as_str(), &mut source)
            .map_err(|error| {
                if source.read_failed() || error.kind() == io::ErrorKind::UnexpectedEof {
                    ArchiveRefusal::ReadFailed
                } else {
                    write_refusal(&error)
                }
            })
    }

    fn finish(self: Box<Self>) -> Result<File, ArchiveRefusal> {
        let encoder = self
            .builder
            .into_inner()
            .map_err(|error| write_refusal(&error))?;
        encoder.finish().map_err(|error| write_refusal(&error))
    }
}
