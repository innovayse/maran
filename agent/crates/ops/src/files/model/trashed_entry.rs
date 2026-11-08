//! One entry of the trash, as a listing reports it.

/// What a listing of the trash says about one entry.
///
/// Built from files the customer can edit, so every field is a claim to show
/// and never one to trust: a restore validates the original path again.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrashedEntry {
    /// The entry's identifier, in the shape the agent writes.
    pub trash_id: String,
    /// Where the entry lived when it was trashed, `/`-separated; empty when the
    /// record of it is missing or unreadable.
    pub original_path: String,
    /// When it was trashed, Unix seconds, read from the identifier.
    pub trashed_at_unix: i64,
    /// Whether the trashed entry is a real directory.
    pub is_directory: bool,
    /// Size of a trashed file in bytes; zero for a directory, which is not
    /// totalled to answer a listing.
    pub size_bytes: u64,
}
