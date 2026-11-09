//! A gzip member made of stored deflate blocks, for the hand-built archives.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use crate::crc32::crc32;

/// `bytes` as one gzip member made of stored deflate blocks.
pub fn gzip_stored(bytes: &[u8]) -> Vec<u8> {
    let mut out = vec![0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 255];
    let mut chunks = bytes.chunks(65_535).peekable();
    if chunks.peek().is_none() {
        out.extend_from_slice(&[1, 0, 0, 0xff, 0xff]);
    }
    while let Some(chunk) = chunks.next() {
        out.push(u8::from(chunks.peek().is_none()));
        let length = u16::try_from(chunk.len()).unwrap();
        out.extend_from_slice(&length.to_le_bytes());
        out.extend_from_slice(&(!length).to_le_bytes());
        out.extend_from_slice(chunk);
    }
    out.extend_from_slice(&crc32(bytes).to_le_bytes());
    out.extend_from_slice(
        &u32::try_from(bytes.len() & 0xFFFF_FFFF)
            .unwrap()
            .to_le_bytes(),
    );
    out
}
