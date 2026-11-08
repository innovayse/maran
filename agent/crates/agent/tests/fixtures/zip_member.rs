//! One member of a hand-built zip archive, and the archives built from members.
//!
//! Everything is STORED (zip method 0), which keeps the encoder to a few lines.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use crate::crc32::crc32;

/// One member of a hand-built zip archive.
pub struct ZipMember {
    /// Name bytes, verbatim.
    pub name: Vec<u8>,
    /// Content.
    pub data: Vec<u8>,
    /// The full `st_mode` recorded in the external attributes.
    pub mode: u32,
}

impl ZipMember {
    /// A regular file of mode 0644.
    pub fn file(name: &str, data: &[u8]) -> Self {
        Self {
            name: name.as_bytes().to_vec(),
            data: data.to_vec(),
            mode: 0o100_644,
        }
    }

    /// A member recorded with the given full `st_mode` (type bits included).
    pub fn with_mode(name: &str, data: &[u8], mode: u32) -> Self {
        Self {
            name: name.as_bytes().to_vec(),
            data: data.to_vec(),
            mode,
        }
    }
}

impl ZipMember {
    pub fn archive(members: &[ZipMember]) -> Vec<u8> {
        let mut out = Vec::new();
        let mut central = Vec::new();
        for member in members {
            let offset = u32::try_from(out.len()).unwrap();
            let crc = crc32(&member.data);
            let size = u32::try_from(member.data.len()).unwrap();
            let name_length = u16::try_from(member.name.len()).unwrap();

            out.extend_from_slice(&[b'P', b'K', 3, 4]);
            out.extend_from_slice(&20_u16.to_le_bytes());
            out.extend_from_slice(&[0, 0, 0, 0, 0, 0, 0, 0]);
            out.extend_from_slice(&crc.to_le_bytes());
            out.extend_from_slice(&size.to_le_bytes());
            out.extend_from_slice(&size.to_le_bytes());
            out.extend_from_slice(&name_length.to_le_bytes());
            out.extend_from_slice(&0_u16.to_le_bytes());
            out.extend_from_slice(&member.name);
            out.extend_from_slice(&member.data);

            central.extend_from_slice(&[b'P', b'K', 1, 2]);
            central.extend_from_slice(&((3_u16 << 8) | 20).to_le_bytes());
            central.extend_from_slice(&20_u16.to_le_bytes());
            central.extend_from_slice(&[0, 0, 0, 0, 0, 0, 0, 0]);
            central.extend_from_slice(&crc.to_le_bytes());
            central.extend_from_slice(&size.to_le_bytes());
            central.extend_from_slice(&size.to_le_bytes());
            central.extend_from_slice(&name_length.to_le_bytes());
            central.extend_from_slice(&[0, 0, 0, 0, 0, 0]);
            central.extend_from_slice(&(member.mode << 16).to_le_bytes());
            central.extend_from_slice(&offset.to_le_bytes());
            central.extend_from_slice(&member.name);
        }

        let directory_offset = u32::try_from(out.len()).unwrap();
        let count = u16::try_from(members.len()).unwrap();
        out.extend_from_slice(&central);
        out.extend_from_slice(&[b'P', b'K', 5, 6, 0, 0, 0, 0]);
        out.extend_from_slice(&count.to_le_bytes());
        out.extend_from_slice(&count.to_le_bytes());
        out.extend_from_slice(&u32::try_from(central.len()).unwrap().to_le_bytes());
        out.extend_from_slice(&directory_offset.to_le_bytes());
        out.extend_from_slice(&0_u16.to_le_bytes());
        out
    }
}
