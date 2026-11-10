//! One database name and size as the source's listing printed them.

/// A database of the source, with a name that has the shape of a MySQL
/// identifier but is NOT yet tied to an account.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ListedDatabase {
    /// The name; `[A-Za-z0-9_$-]`, at most 64 bytes.
    pub name: String,
    /// The size in bytes, or `None` when the listing gave none.
    pub size_bytes: Option<u64>,
}
