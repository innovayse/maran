//! One object a destination holds, as a listing sees it.

/// An object in a backup destination: its key and its size.
///
/// Deliberately two fields and not a mirror of whatever the transport returns.
/// A destination's listing is used for exactly two things — deciding which
/// backups exist, and deciding which of them retention prunes — and both are
/// answered by a key and a size. Carrying the provider's `last_modified`, its
/// ETag or its storage class would put facts into this area that nothing reads,
/// and each of them is a fact the LOCAL destination would have to invent an
/// answer for so that the seam's two implementations kept agreeing.
///
/// The key is the whole key, exactly as the destination stores it — prefix
/// included. A caller that wants the backup id takes it from the key's last
/// segment, which is the same operation for both implementations because
/// [`object_key`](crate::backup::object_key) built both.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ObjectSummary {
    /// The object's full key, prefix included.
    pub key: String,

    /// The object's size in bytes.
    pub bytes: u64,
}
