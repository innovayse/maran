//! Everything one file write is told, already validated.

use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

/// A validated request to write one file inside an account's home.
///
/// Every field except the content is a type that cannot be constructed from
/// anything invalid, so no layer below re-parses any of them (rules/rust.md
/// "Validation first"). `mode` was the exception until review: a loose `u32`
/// refused by a hand-written `if` in two separate layers, which is the weaker
/// half of the same pattern `RelativePath` follows one line above it.
///
/// The content is owned rather than borrowed because it crosses a `fork`: the
/// child that writes it is a copy of this process, and the bytes must already
/// be in memory when the fork happens. Nothing is read from the wire after
/// that point.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WriteFileInput {
    /// The account whose uid the write runs as, and whose home it is contained
    /// in.
    pub account: AccountName,
    /// Where the file goes, relative to that home.
    pub path: RelativePath,
    /// The bytes to write, exactly as they will appear on disk.
    ///
    /// Bytes and not a `String`: the contract says a file, and a file is bytes.
    /// The one caller today writes an ASCII key authorization, which a
    /// certificate authority compares octet for octet.
    pub contents: Vec<u8>,
    /// The permission bits the finished file carries, e.g. `0o644`.
    ///
    /// Applied to the descriptor after the write and before the rename, so the
    /// file is never briefly readable by more than it should be, and so the
    /// daemon's umask cannot quietly narrow it.
    pub mode: FileMode,
}
