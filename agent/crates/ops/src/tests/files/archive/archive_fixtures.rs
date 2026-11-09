//! Builders of archives for the tests, including the hostile ones.
//!
//! The archive crates refuse to write the cases that matter here (a `..` member,
//! an absolute name, a device node), so tar archives are assembled from raw
//! 512-byte headers, and zip archives are written through the crate with the
//! name and the unix mode set verbatim (`external_attributes` carries the file
//! type bits, which `unix_permissions` masks away).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Cursor, Write};

use flate2::Compression;
use flate2::write::GzEncoder;
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

/// Tar type flag of a regular file.
pub(crate) const TAR_FILE: u8 = b'0';
/// Tar type flag of a hard link.
pub(crate) const TAR_HARDLINK: u8 = b'1';
/// Tar type flag of a symbolic link.
pub(crate) const TAR_SYMLINK: u8 = b'2';
/// Tar type flag of a character device.
pub(crate) const TAR_CHARACTER_DEVICE: u8 = b'3';
/// Tar type flag of a directory.
pub(crate) const TAR_DIRECTORY: u8 = b'5';
/// Tar type flag of a FIFO.
pub(crate) const TAR_FIFO: u8 = b'6';
/// Tar type flag of a GNU sparse file.
pub(crate) const TAR_SPARSE: u8 = b'S';

/// One member of a hand-built tar archive.
pub(crate) struct TarMember {
    /// The name bytes, written verbatim (at most 100).
    pub(crate) name: Vec<u8>,
    /// The type flag.
    pub(crate) kind: u8,
    /// The content of a regular file.
    pub(crate) data: Vec<u8>,
    /// The link target of a link.
    pub(crate) link: String,
    /// The permission bits.
    pub(crate) mode: u32,
}

impl TarMember {
    /// A regular file of mode 0644.
    pub(crate) fn file(name: &str, data: &[u8]) -> Self {
        Self::of(name, TAR_FILE, data, "", 0o644)
    }

    /// A regular file of the given mode.
    pub(crate) fn file_with_mode(name: &str, data: &[u8], mode: u32) -> Self {
        Self::of(name, TAR_FILE, data, "", mode)
    }

    /// A directory.
    pub(crate) fn directory(name: &str) -> Self {
        Self::of(name, TAR_DIRECTORY, b"", "", 0o755)
    }

    /// A member of any type, with no content.
    pub(crate) fn special(name: &str, kind: u8) -> Self {
        Self::of(name, kind, b"", "", 0o644)
    }

    /// A link of the given type flag to `target`.
    pub(crate) fn link(name: &str, kind: u8, target: &str) -> Self {
        Self::of(name, kind, b"", target, 0o777)
    }

    /// A member whose name bytes are exactly `name`.
    pub(crate) fn raw_named(name: &[u8], data: &[u8]) -> Self {
        Self {
            name: name.to_vec(),
            kind: TAR_FILE,
            data: data.to_vec(),
            link: String::new(),
            mode: 0o644,
        }
    }

    fn of(name: &str, kind: u8, data: &[u8], link: &str, mode: u32) -> Self {
        Self {
            name: name.as_bytes().to_vec(),
            kind,
            data: data.to_vec(),
            link: link.to_owned(),
            mode,
        }
    }
}

/// Copies `value` into the start of `field`, truncating.
fn put(field: &mut [u8], value: &[u8]) {
    let length = value.len().min(field.len());
    field[..length].copy_from_slice(&value[..length]);
}

/// The 512-byte header of `member`.
fn header(member: &TarMember) -> [u8; 512] {
    let mut block = [0_u8; 512];
    put(&mut block[0..100], &member.name);
    put(
        &mut block[100..108],
        format!("{:07o}\0", member.mode).as_bytes(),
    );
    put(&mut block[108..116], b"0000000\0");
    put(&mut block[116..124], b"0000000\0");
    put(
        &mut block[124..136],
        format!("{:011o}\0", member.data.len()).as_bytes(),
    );
    put(&mut block[136..148], b"00000000000\0");
    block[148..156].fill(b' ');
    block[156] = member.kind;
    put(&mut block[157..257], member.link.as_bytes());
    if member.kind == TAR_SPARSE {
        put(&mut block[257..265], b"ustar  \0");
    } else {
        put(&mut block[257..263], b"ustar\0");
        put(&mut block[263..265], b"00");
    }
    let sum: u32 = block.iter().map(|byte| u32::from(*byte)).sum();
    put(&mut block[148..156], format!("{sum:06o}\0 ").as_bytes());
    block
}

/// A tar archive of `members`, ended with the two zero blocks.
pub(crate) fn tar_bytes(members: &[TarMember]) -> Vec<u8> {
    let mut bytes = Vec::new();
    for member in members {
        bytes.extend_from_slice(&header(member));
        bytes.extend_from_slice(&member.data);
        let padding = (512 - member.data.len() % 512) % 512;
        bytes.extend(std::iter::repeat_n(0_u8, padding));
    }
    bytes.extend(std::iter::repeat_n(0_u8, 1024));
    bytes
}

/// `bytes` as one gzip member.
pub(crate) fn gzip(bytes: &[u8]) -> Vec<u8> {
    let mut encoder = GzEncoder::new(Vec::new(), Compression::new(6));
    encoder.write_all(bytes).unwrap();
    encoder.finish().unwrap()
}

/// A `.tar.gz` of `members`.
pub(crate) fn tar_gz_bytes(members: &[TarMember]) -> Vec<u8> {
    gzip(&tar_bytes(members))
}

/// One member of a zip archive.
pub(crate) struct ZipMember {
    /// The name, verbatim; a trailing `/` makes a directory.
    pub(crate) name: String,
    /// The content of a file.
    pub(crate) data: Vec<u8>,
    /// The full `st_mode` (type bits included) to record, or `None` for none.
    pub(crate) mode: Option<u32>,
    /// Whether to store instead of deflate.
    pub(crate) stored: bool,
}

impl ZipMember {
    /// A deflated file with an ordinary mode.
    pub(crate) fn file(name: &str, data: &[u8]) -> Self {
        Self {
            name: name.to_owned(),
            data: data.to_vec(),
            mode: Some(0o100_644),
            stored: false,
        }
    }

    /// A member recorded with the given `st_mode`.
    pub(crate) fn with_mode(name: &str, data: &[u8], mode: u32) -> Self {
        Self {
            name: name.to_owned(),
            data: data.to_vec(),
            mode: Some(mode),
            stored: true,
        }
    }

    /// A directory entry.
    pub(crate) fn directory(name: &str) -> Self {
        Self {
            name: format!("{}/", name.trim_end_matches('/')),
            data: Vec::new(),
            mode: Some(0o040_755),
            stored: true,
        }
    }
}

/// A zip archive of `members`.
pub(crate) fn zip_bytes(members: &[ZipMember]) -> Vec<u8> {
    let mut writer = ZipWriter::new(Cursor::new(Vec::new()));
    for member in members {
        let mut options = SimpleFileOptions::DEFAULT.compression_method(if member.stored {
            CompressionMethod::Stored
        } else {
            CompressionMethod::Deflated
        });
        if let Some(mode) = member.mode {
            options = options.external_attributes(mode << 16);
        }
        writer.start_file(member.name.as_str(), options).unwrap();
        writer.write_all(&member.data).unwrap();
    }
    writer.finish().unwrap().into_inner()
}

/// Sets `flags` bits in the general-purpose flag of every central directory record.
pub(crate) fn set_zip_flag_bits(bytes: &mut [u8], flags: u16) {
    for at in central_records(bytes) {
        let current = u16::from_le_bytes([bytes[at + 8], bytes[at + 9]]);
        let patched = (current | flags).to_le_bytes();
        bytes[at + 8] = patched[0];
        bytes[at + 9] = patched[1];
    }
}

/// Sets the compression method of every central directory record.
pub(crate) fn set_zip_method(bytes: &mut [u8], method: u16) {
    for at in central_records(bytes) {
        let patched = method.to_le_bytes();
        bytes[at + 10] = patched[0];
        bytes[at + 11] = patched[1];
    }
}

/// Sets the declared uncompressed size of every central directory record.
pub(crate) fn set_zip_declared_size(bytes: &mut [u8], size: u32) {
    for at in central_records(bytes) {
        bytes[at + 24..at + 28].copy_from_slice(&size.to_le_bytes());
    }
}

/// Offsets of the central directory records (`PK\x01\x02`).
fn central_records(bytes: &[u8]) -> Vec<usize> {
    bytes
        .windows(4)
        .enumerate()
        .filter(|(_, window)| *window == [b'P', b'K', 1, 2])
        .map(|(at, _)| at)
        .collect()
}

/// Declares `count` entries in the end-of-central-directory record, changing nothing else.
pub(crate) fn set_zip_declared_entries(bytes: &mut [u8], count: u16) {
    let at = bytes
        .windows(4)
        .rposition(|window| window == [b'P', b'K', 5, 6])
        .unwrap();
    let patched = count.to_le_bytes();
    bytes[at + 10] = patched[0];
    bytes[at + 11] = patched[1];
}

/// Writes `bytes` to a temporary file and opens it; the directory must outlive the file.
pub(crate) fn archive_file(bytes: &[u8]) -> (tempfile::TempDir, std::fs::File, u64) {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("archive");
    std::fs::write(&path, bytes).unwrap();
    (
        directory,
        std::fs::File::open(path).unwrap(),
        bytes.len() as u64,
    )
}

/// Every file and directory below `root`, as `relative path -> content` (`None` for a directory).
///
/// Links are reported as `Some(b"-> target")` and are not followed, so a test can
/// tell a link from the file it points at.
pub(crate) fn tree_of(
    root: &std::path::Path,
) -> std::collections::BTreeMap<String, Option<Vec<u8>>> {
    fn visit(
        root: &std::path::Path,
        directory: &std::path::Path,
        into: &mut std::collections::BTreeMap<String, Option<Vec<u8>>>,
    ) {
        for entry in std::fs::read_dir(directory).unwrap() {
            let entry = entry.unwrap();
            let path = entry.path();
            let relative = path
                .strip_prefix(root)
                .unwrap()
                .to_str()
                .unwrap()
                .to_owned();
            let kind = std::fs::symlink_metadata(&path).unwrap().file_type();
            if kind.is_symlink() {
                let target = std::fs::read_link(&path).unwrap();
                into.insert(
                    relative,
                    Some(format!("-> {}", target.display()).into_bytes()),
                );
            } else if kind.is_dir() {
                into.insert(relative, None);
                visit(root, &path, into);
            } else {
                into.insert(relative, Some(std::fs::read(&path).unwrap()));
            }
        }
    }

    let mut into = std::collections::BTreeMap::new();
    visit(root, root, &mut into);
    into
}
