//! What a verification established about one artifact, and nothing it did not.

/// The facts [`verify_backup`](crate::backup::verify_backup) established by
/// reading one artifact and its sidecar.
///
/// A value of this type exists only after EVERY check ran to completion, so it
/// is never the answer to "nothing went wrong while looking". Each field is a
/// count of something that was actually established, and a zero is a measured
/// zero:
///
/// - the artifact hashed to the digest its sidecar recorded and weighed what the
///   sidecar said,
/// - its member list was read whole, within the layout and the bounds a restore
///   applies,
/// - its manifest parsed, named this account and this backup, and agreed with
///   the sidecar,
/// - and every database the manifest names has its dump member in the listing.
///
/// What it does NOT establish is written on `verify_backup` and repeated here
/// because this is the type a caller holds: that the dumps load, that the files
/// are the customer's intended files, that the host has room to restore, that a
/// remote copy exists, or anything against a replacement of the artifact AND its
/// sidecar by someone who can write root-owned files. It is a statement about
/// the bytes at the moment of the read and not a promise about a restore.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BackupVerification {
    /// How many members the artifact's listing held, directories included.
    pub member_count: u32,

    /// How many dumps the manifest names whose member was found in the listing.
    ///
    /// Equal to the number of databases the manifest lists, because a missing
    /// dump is a refusal and never a smaller count. The dumps' CONTENT is not
    /// read (see `verify_backup`), so this counts presence, not a digest match.
    pub database_count: u32,

    /// The artifact's size in bytes, as the read measured it (and equal to the
    /// size the sidecar recorded, or the verification would have been refused).
    pub artifact_bytes: u64,
}
