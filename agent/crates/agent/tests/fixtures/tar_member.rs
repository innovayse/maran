//! One member of a hand-built tar archive, and the archives built from members.
//!
//! Hand-built on purpose and with no dependency on `zip`, `tar` or `flate2`: a
//! fixture that used the crate under test to make its input would agree with
//! the crate about every name it considered fine. Everything is STORED (gzip
//! stored blocks), which keeps the encoders to a few lines.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use crate::gzip_stored::gzip_stored;

/// Tar type flag of a regular file.
pub const TAR_FILE: u8 = b'0';
/// Tar type flag of a symbolic link.
pub const TAR_SYMLINK: u8 = b'2';
/// Tar type flag of a character device.
pub const TAR_DEVICE: u8 = b'3';
/// Tar type flag of a directory.
pub const TAR_DIRECTORY: u8 = b'5';
/// Tar type flag of a FIFO.
pub const TAR_FIFO: u8 = b'6';

/// One member of a hand-built tar archive.
pub struct TarMember {
    /// Name bytes, verbatim (at most 100).
    pub name: Vec<u8>,
    /// Type flag.
    pub kind: u8,
    /// Content of a regular file.
    pub data: Vec<u8>,
    /// Link target.
    pub link: String,
    /// Permission bits.
    pub mode: u32,
}

impl TarMember {
    /// A regular file of mode 0644.
    pub fn file(name: &str, data: &[u8]) -> Self {
        Self::of(name, TAR_FILE, data, "", 0o644)
    }

    /// A regular file of the given mode.
    pub fn file_with_mode(name: &str, data: &[u8], mode: u32) -> Self {
        Self::of(name, TAR_FILE, data, "", mode)
    }

    /// A directory.
    pub fn directory(name: &str) -> Self {
        Self::of(name, TAR_DIRECTORY, b"", "", 0o755)
    }

    /// A member of any type with no content.
    pub fn special(name: &str, kind: u8) -> Self {
        Self::of(name, kind, b"", "", 0o644)
    }

    /// A symbolic link to `target`.
    pub fn symlink(name: &str, target: &str) -> Self {
        Self::of(name, TAR_SYMLINK, b"", target, 0o777)
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
fn tar_header(member: &TarMember) -> [u8; 512] {
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
    put(&mut block[257..263], b"ustar\0");
    put(&mut block[263..265], b"00");
    let sum: u32 = block.iter().map(|byte| u32::from(*byte)).sum();
    put(&mut block[148..156], format!("{sum:06o}\0 ").as_bytes());
    block
}

impl TarMember {
    pub fn archive(members: &[TarMember]) -> Vec<u8> {
        let mut bytes = Vec::new();
        for member in members {
            bytes.extend_from_slice(&tar_header(member));
            bytes.extend_from_slice(&member.data);
            bytes.extend(std::iter::repeat_n(
                0_u8,
                (512 - member.data.len() % 512) % 512,
            ));
        }
        bytes.extend(std::iter::repeat_n(0_u8, 1024));
        bytes
    }

    pub fn gzipped_archive(members: &[TarMember]) -> Vec<u8> {
        gzip_stored(&Self::archive(members))
    }
}
