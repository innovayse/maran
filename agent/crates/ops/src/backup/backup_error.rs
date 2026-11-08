//! Why a backup could not be created.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::floor_refusal::FloorRefusal;

use crate::backup::model::dump_refusal::DumpRefusal;

/// The `status` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `status: -1` knows the program never
/// ran, rather than looking up status -1 in `tar`'s manual.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while creating a backup of an account.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a
/// deliberately narrow one: **no variant carries a program's output or a
/// database's contents.** Every field of a creation's variants is an integer,
/// except [`BackupError::Bound`], whose payload names the program's own
/// absolute path (an agent-derived string) and the bound that stopped it.
///
/// A restore's variants had to widen that, because a restore's failures are
/// about a live account and "one of your databases could not be put back" with
/// no name is not an answer an operator can act on. The widening is bounded to
/// exactly two kinds of string and neither is tool output:
///
/// - **Names this agent already knows** — a database from the panel's own
///   `allowed_databases`, or a staging path built by `AgentPaths`. Both are
///   agent-derived and neither can hold a control character.
/// - **One name that came out of a customer-supplied archive**
///   ([`BackupError::UnexpectedArchiveMember`]). That one is rendered through
///   `refused_member_name` before it is stored, which escapes every
///   non-printable byte and truncates — an archive member name is attacker-chosen
///   text about to enter an operator's log, and an embedded newline there is
///   rules/security.md item 4's problem wearing a different hat.
///
/// That shape is specific to this area rather than inherited caution. The dump
/// client is handed a customer's database, and a client that refuses partway
/// prints rows, table names and occasionally the values it choked on. A variant
/// able to hold that output would put a customer's data into the operator log
/// and into every error path above it (rules/security.md item 8). A shape that
/// cannot hold a string cannot hold it.
///
/// The cost to an operator is real and accepted: a refusal that is not one of
/// the named conditions arrives as an exit status and nothing else. The status
/// is enough to find the condition in the tool's manual, and the agent's own
/// audit record supplies the rest.
///
/// # The third kind of string, and what pays for it
///
/// [`BackupError::ObjectStoreFailed`] carries a REMOTE PROVIDER's own words,
/// which is neither of the two kinds above. It exists because a bucket's
/// refusals are not a small enumerable set the way `tar`'s exit statuses are —
/// a bucket that does not exist, a region that does not match, an endpoint
/// whose certificate is wrong, a clock skew of eleven minutes and a policy
/// denying one verb are five different things an operator fixes differently,
/// and collapsing them into `RemoteFailed` would leave nothing to act on.
///
/// What pays for it is a redactor, not a hope. Every message reaching this
/// variant has been through `s3_object_store_host`'s `redact`, which removes
/// the configured access key id and every `X-Amz-Signature=` value — the
/// provider's text quotes the request that failed, and a signed request carries
/// both. It is still not a home for tool output or customer rows: nothing
/// constructs it from a spawned program's stderr, and nothing may start.
#[derive(Debug, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum BackupError {
    /// A program a backup or restore needs to spawn is not present and
    /// executable at the path this host's distro adapter declared for it.
    ///
    /// Raised by [`crate::backup::verify_backup_binaries`], never by a spawn
    /// itself: a spawn failure is [`Self::ArchiveFailed`],
    /// [`Self::DumpFailed`] or [`Self::DropFailed`]/[`Self::LoadFailed`] with
    /// `PROGRAM_UNAVAILABLE`, all of which only occur mid-operation. This
    /// variant exists so the SAME absence can be reported before an operation
    /// starts, naming the program by name and the path that was checked —
    /// "a required program is missing" sends an operator hunting among three
    /// candidates and two families; this does not.
    #[error("{program} is required for backups but is not executable at {path}")]
    BackupBinaryMissing {
        /// The program's name (`tar`, `gzip`, or `database dump client`), not
        /// its path — the path is the second field, so a reader gets both
        /// without parsing the message.
        program: String,
        /// The absolute path this host's distro adapter declared for
        /// `program`, and the exact path that was found missing or
        /// non-executable.
        path: String,
    },

    /// This account already has a backup or restore running.
    ///
    /// Two creations of one account interleave two `tar` runs over one home and
    /// two dump sets in one scratch, so the second is refused rather than
    /// queued: the panel retries, and a retry that waited would hold an RPC
    /// open for as long as the first backup takes.
    #[error("a backup or restore is already running for this account")]
    AlreadyRunning,

    /// An artifact with this backup id is already published for this account.
    ///
    /// The idempotent answer to a repeated creation, decided BEFORE any dump is
    /// taken: a retry of a creation whose response was lost must not spend an
    /// hour re-dumping a database to overwrite an artifact that is already
    /// correct.
    #[error("that backup already exists")]
    AlreadyExists,

    /// The configured backup root is not what the string said it was.
    ///
    /// A component resolved through a symlink, it is not a directory, it is not
    /// owned by root, or it is readable or writable by anyone but root. The
    /// string was checked when the operator configured it; this is the INODE's
    /// answer, taken immediately before the write, and the two are different
    /// questions.
    #[error("the backup root's ownership or mode is not root-only")]
    BackupRootUnsafe {
        /// The uid that owns the directory the check refused. `0` here means
        /// the refusal was about the mode rather than the owner.
        uid: u32,
        /// The permission bits it carried, or `0` when the path could not be
        /// stated as a directory at all.
        mode: u32,
    },

    /// The backup root or the account's directory under it could not be
    /// created, read or written.
    #[error("the backup root is unusable")]
    BackupRootUnusable,

    /// The account's backup directory holds something wearing an artifact's
    /// `.tar.gz` extension whose name this agent could never have minted:
    /// either the name is not valid UTF-8, or its stem is not a backup id.
    ///
    /// Refused rather than listed, and refused rather than skipped. Skipping
    /// is the invisible-to-retention outcome this area refuses everywhere
    /// else, and listing is worse: `backup_id` is the field the panel sends
    /// straight back in a delete or a restore, so a listing that put a file
    /// name there would be handing every later caller an identifier that is
    /// not one. Refusing names the offending entry, once, to the operator who
    /// can remove it — and the directory is root-only, so nothing but root
    /// put it there.
    ///
    /// An entry that merely does not wear the artifact extension — a sidecar,
    /// a `.partial`, an operator's notes — is not this and is not refused;
    /// this directory has never claimed to hold nothing but artifacts.
    #[error("the backup directory holds an artifact name this agent never minted: {name}")]
    UnmintedArtifactName {
        /// The offending file name, lossily converted when it is not UTF-8 —
        /// which is itself one of the two ways to reach this error, so the
        /// conversion is reporting the problem rather than hiding it.
        name: String,
    },

    /// The root-only scratch directory could not be created or removed.
    #[error("the backup scratch directory is unusable")]
    ScratchUnusable,

    /// The filesystem the scratch sits on could not be asked how much room it
    /// has.
    ///
    /// Refused rather than defaulted, and that is the whole point of the
    /// measurement: a ceiling that falls back to a generous number when it
    /// could not measure is a ceiling that disappears exactly when the
    /// filesystem is unhealthy, which is the moment it was the only thing
    /// between a bulk write and a full disk.
    #[error("the free space on the backup scratch filesystem could not be measured")]
    ScratchUnmeasurable,

    /// The scratch filesystem does not have room for what is about to be
    /// written into it.
    ///
    /// Refused BEFORE the write, which is what separates this from
    /// [`BackupError::DumpTooLarge`]: that one is measured after a client has
    /// finished writing and can only report a write it already permitted.
    #[error("the backup scratch has {available} bytes free and needs {required}")]
    ScratchTooSmall {
        /// What an unprivileged writer could still add to that filesystem.
        available: u64,
        /// What the operation is about to write into it.
        required: u64,
    },

    /// The backup or restore was refused at the free-space floor before it
    /// wrote anything (issue #148): a filesystem it writes into has less than
    /// the floor available, or could not be measured.
    #[error(transparent)]
    FreeSpace(#[from] FloorRefusal),

    /// The catalog could not say which databases this account owns.
    ///
    /// A creation refuses rather than backing up the files alone: an archive
    /// whose manifest lists no databases is indistinguishable from one taken of
    /// an account that genuinely has none, and a restore would then quietly
    /// leave the live databases alone.
    #[error("the account's databases could not be listed")]
    DatabasesUnknown,

    /// The dump client refused, or could not be run at all.
    ///
    /// Whatever it had already written is deleted with the scratch: a partial
    /// dump is not a smaller backup, it is a backup that restores a truncated
    /// database over a working one.
    #[error("the database dump client failed with status {status}")]
    DumpFailed {
        /// The client's exit status, or `PROGRAM_UNAVAILABLE`.
        status: i32,
    },

    /// One dump is larger than the ceiling this agent will archive.
    #[error("a database dump exceeded the ceiling: {actual} bytes over {limit}")]
    DumpTooLarge {
        /// The ceiling, in bytes.
        limit: u64,
        /// What the dump actually weighed, in bytes.
        actual: u64,
    },

    /// `tar` refused, or could not be run at all.
    #[error("the archiver failed with status {status}")]
    ArchiveFailed {
        /// `tar`'s exit status, or `PROGRAM_UNAVAILABLE`.
        status: i32,
    },

    /// The finished archive is larger than the ceiling this agent will publish.
    #[error("the archive exceeded the ceiling: {actual} bytes over {limit}")]
    ArchiveTooLarge {
        /// The ceiling, in bytes.
        limit: u64,
        /// What the archive actually weighed, in bytes.
        actual: u64,
    },

    /// The manifest could not be built or written into the scratch.
    #[error("the manifest could not be written")]
    ManifestUnwritable,

    /// A file that had to be hashed could not be read to the end.
    ///
    /// Reported rather than treated as a zero-length file: an unread file has
    /// no checksum, and recording the digest of nothing would make a restore
    /// verify an archive against a number that describes no bytes.
    #[error("a file could not be read for checksumming")]
    ChecksumUnreadable,

    /// The account's home could not be read to measure it.
    #[error("the account's home could not be measured")]
    HomeUnreadable,

    /// The finished archive could not be published onto its final name.
    ///
    /// The artifact stays a `.partial` and no sidecar is written, so nothing
    /// downstream can mistake it for a backup.
    #[error("the artifact could not be published")]
    ArtifactUnpublishable,

    /// No artifact with this id is published for this account.
    ///
    /// Reported for a delete of a backup that never existed or was already
    /// removed — an outcome, not a failure (rules/rust.md "idempotent"). A
    /// caller reads it as a converged retry: deleting the same id twice is
    /// safe, and the second call costs one `stat`.
    #[error("no such backup")]
    NotFound,

    /// A present artifact, or its sidecar, could not be removed.
    ///
    /// Distinct from [`Self::ArtifactUnpublishable`], which is about a
    /// creation that never finished: this is about deleting a backup that
    /// finished and is on disk, and the two failures have nothing in common
    /// but the file they touch.
    #[error("the backup could not be deleted")]
    ArtifactUndeletable,
    /// The artifact on disk is not the artifact the panel recorded.
    ///
    /// Refused in step 1, before the pre-restore backup and long before any
    /// drop. The bytes about to be unpacked as root and loaded as the database
    /// superuser are not the bytes this panel took.
    #[error("the artifact's checksum does not match the one recorded for it")]
    ChecksumMismatch,

    /// The manifest inside the archive is a version this agent does not know.
    ///
    /// Refused rather than read on the fields it recognises: a restore that
    /// proceeded would silently skip whatever a newer version added, which for
    /// this document means silently skipping databases.
    #[error("the archive's manifest version is not one this agent understands")]
    ManifestVersionUnknown,

    /// The manifest names a different account from the one being restored.
    #[error("the archive belongs to a different account")]
    ManifestAccountMismatch,

    /// The manifest inside the archive and the sidecar beside it disagree.
    ///
    /// The sidecar is the cheap copy a listing reads, and the copy an editor
    /// can reach without touching the artifact; the copy inside the archive is
    /// the authority. They are compared so that editing the cheap one is
    /// useless to whoever tries it.
    #[error("the archive's manifest disagrees with its sidecar")]
    ManifestDisagreesWithSidecar,

    /// The archive holds a member outside `manifest.json`, `home/` and
    /// `databases/`.
    ///
    /// Refused, never skipped: a member this agent cannot place is an archive
    /// it does not understand, and the interesting cases — `../../etc/cron.d/…`,
    /// an absolute path — are exactly the ones a "skip what you do not know"
    /// reading would let through to whatever unpacked it next.
    #[error("the archive holds a member outside its layout: {name}")]
    UnexpectedArchiveMember {
        /// The member's name, escaped and truncated by `refused_member_name`.
        name: String,
    },

    /// The manifest names a database the panel no longer knows about.
    ///
    /// Refused and **never created**. Creating it would resurrect a database
    /// the panel has forgotten, owned by a user nothing points at — an orphan
    /// on the server with no row anywhere saying who may reach it.
    #[error("the archive names a database this panel does not know: {name}")]
    UnknownDatabase {
        /// The database's name as the manifest spells it, escaped by
        /// `refused_member_name` because a manifest is archive content too.
        name: String,
    },

    /// An extracted dump is not the dump the manifest recorded.
    ///
    /// Checked after extraction and BEFORE any drop, because the loader
    /// connects as the database superuser and a dump that is not this backup's
    /// is arbitrary SQL with that authority.
    #[error("the extracted dump for {database} does not match its recorded checksum")]
    DumpChecksumMismatch {
        /// The database whose dump failed the check — a name the panel supplied
        /// in `allowed_databases`, so agent-known.
        database: String,
    },

    /// A database could not be dropped.
    #[error("a database could not be dropped, status {status}")]
    DropFailed {
        /// The client's exit status, or `PROGRAM_UNAVAILABLE`.
        status: i32,
    },

    /// The server's temporary import users could not be listed, so none left
    /// behind by an interrupted import was dropped.
    #[error("the temporary import users could not be listed, status {status}")]
    ImportUsersUnlistable {
        /// The client's exit status, or `PROGRAM_UNAVAILABLE`.
        status: i32,
    },

    /// A dump could not be loaded back into the server.
    #[error("a dump could not be loaded, status {status}")]
    LoadFailed {
        /// The client's exit status, or `PROGRAM_UNAVAILABLE`.
        status: i32,
    },

    /// A dump someone else wrote was refused by the restricted loader's pre-scan,
    /// before a byte of it reached the database server.
    ///
    /// A finding about the dump, not about the host: nothing was loaded and no
    /// temporary user was created. Carries a closed kind and nothing the dump
    /// said.
    #[error("a dump was refused before it reached the server: {reason}")]
    RestrictedLoadRefused {
        /// Why the pre-scan refused it.
        reason: DumpRefusal,
    },

    /// The restore's staging tree could not be created, chowned or removed.
    #[error("the restore staging directory is unusable")]
    StagingUnusable,

    /// The identity an extraction had to run as could not be entered.
    ///
    /// The account does not resolve to a uid, or the fork that drops to it
    /// failed. Reported rather than fallen back from: an extraction of a
    /// customer's archive that runs as root because dropping did not work is
    /// precisely what R3 exists to prevent.
    #[error("the account's identity could not be entered for the extraction")]
    ExtractionIdentityUnavailable,

    /// The account's numeric identity changed while the restore was running.
    ///
    /// A restore resolves the account's uid and gid at its start, fills a
    /// staging tree as that identity, and hours later renames the tree into
    /// place and chowns the result. Those ids are re-read immediately before
    /// the swap and compared; a difference means the account this restore is
    /// for is no longer the account those numbers name.
    ///
    /// Refused rather than carried on with, because the alternative is the one
    /// outcome a hosting panel must never produce: `userdel` frees a uid,
    /// `useradd` gives the lowest free one to the next account, and a home
    /// chowned to a remembered number is one customer's files under another
    /// customer's identity. The refusal lands BEFORE the first rename, so
    /// nothing has been swapped, the staging tree is removed with the rest, and
    /// the account's databases have already been replaced — which is why this
    /// is a system failure and not a validation refusal.
    #[error("the account's identity changed while the restore was running")]
    AccountIdentityChanged,

    /// A restore failed after the point of no return, and every database it had
    /// already replaced was put back from its rollback dump.
    ///
    /// The account's databases are the ones it had before this operation
    /// started, and its files were never touched — the file swap happens after
    /// this stage, so there is nothing to reverse there.
    ///
    /// It carries [`Self::RolledBack::rolled_back`] because on a failing path
    /// the error is the ONLY thing that crosses back: `restore_backup` returns
    /// `Err` and no [`RestoreOutcome`](crate::backup::RestoreOutcome) at all,
    /// so a list built into an outcome value on this path would be dropped
    /// where it was written. That is what used to happen, and a mutation
    /// emptying the list left the whole suite green because nothing could read
    /// it.
    #[error(
        "the restore failed at {failed} and every replaced database was rolled back: \
         {rolled_back:?}"
    )]
    RolledBack {
        /// The database the restore failed on.
        failed: String,
        /// The databases put back from their own rollback dump, in the order
        /// they were put back — which is the reverse of the order they were
        /// replaced in.
        rolled_back: Vec<String>,
    },

    /// A restore failed after the point of no return and the rollback ITSELF
    /// failed for at least one database.
    ///
    /// The worst outcome this operation can produce, and it is its own variant
    /// so that it can never be read as the ordinary one. The named databases
    /// hold neither the state the customer had nor the state they asked for,
    /// and an operator has to go to the pre-restore backup of step 2.
    #[error("the restore failed at {failed} and could not roll back: {not_rolled_back:?}")]
    RolledBackPartially {
        /// The database the restore failed on.
        failed: String,
        /// The databases that WERE put back from their own rollback dump.
        ///
        /// Carried beside [`Self::RolledBackPartially::not_rolled_back`]
        /// because an operator handed only the failures cannot tell a database
        /// that was restored from one this operation never reached — and the
        /// two need different actions. Neither list is derivable from the
        /// other: the databases this restore had replaced by the time it failed
        /// are not the databases the request named.
        rolled_back: Vec<String>,
        /// The databases whose rollback dump could not be reloaded.
        not_rolled_back: Vec<String>,
    },

    /// The home was renamed aside, the staging tree could not take its place,
    /// and the reversal failed too.
    ///
    /// The account has no home at the path everything else on this host expects
    /// one at. Where it is parked is carried, because a message an operator can
    /// act on beats a rollback that lies — one `mv` puts the customer back, and
    /// the alternative is an error saying a home is missing without saying
    /// where it went. The path is `AgentPaths`-built, never caller input.
    #[error("the restore left the account's home parked at {path}")]
    HomeParkedAt {
        /// Where the home is.
        path: String,
    },

    /// A single directory was renamed aside, the restored one could not take its
    /// place, and the reversal failed too (issue #160).
    ///
    /// The directory the customer's site is served from is missing from its
    /// home, and its previous contents are parked at the carried path. The path
    /// is `AgentPaths`-built (the restore's own parking directory), never caller
    /// input, so one `mv` puts the site back.
    #[error("the restore left the site's directory parked at {path}")]
    DirectoryParkedAt {
        /// Where the previous directory is.
        path: String,
    },

    /// A directory on the way to the one being restored, or one of the restore's own
    /// working directories, is missing, is a link, or is not the account's.
    ///
    /// Raised by the descriptor walk of a single-directory restore (issue #160),
    /// which refuses what it cannot prove is the account's own rather than
    /// following it. Nothing was replaced.
    #[error("a directory the restore needs is missing, is a link, or is not the account's")]
    DirectoryUnusable,

    /// The archive does not hold the database the restore was asked for.
    ///
    /// The database is one the panel knows (otherwise it would be
    /// [`Self::UnknownDatabase`]), but the manifest does not list it: it was
    /// created after the backup was taken. Nothing was changed.
    #[error("the archive holds no dump of the requested database")]
    DatabaseNotInBackup,

    /// The archive does not hold the directory the restore was asked for.
    ///
    /// The directory is absent from the archive's `home/` tree: the site did not
    /// exist when the backup was taken. Nothing was changed.
    #[error("the archive holds no copy of the requested directory")]
    DirectoryNotInBackup,

    /// The destination's endpoint is not HTTPS.
    ///
    /// Refused at construction, so no operation exists that could use it. What
    /// crosses the wire is every file in a customer's home and a full dump of
    /// every database they own, plus a signature good for every other object in
    /// the bucket; over `http://` both are readable by anything on the path
    /// (rules/security.md item 10). A custom HTTPS endpoint is welcome — that
    /// is how a provider other than AWS is reached — the scheme is not.
    #[error("the destination's endpoint is not https")]
    DestinationInsecure,

    /// The destination does not hold the object that was asked for.
    ///
    /// Its own variant rather than an [`Self::ObjectStoreFailed`] carrying a
    /// message, because callers BRANCH on it: a delete treats it as success and
    /// a restore treats it as a refusal, and neither may reach that decision by
    /// reading English out of a string.
    #[error("the destination does not hold that object")]
    ObjectNotFound,

    /// The destination refused, or could not be reached.
    ///
    /// The message is the provider's, **after** redaction of the access key id
    /// and of any signature — see the type's note above on why this variant
    /// carries a provider's words and what pays for it.
    #[error("the backup destination failed: {message}")]
    ObjectStoreFailed {
        /// The redacted reason.
        message: String,
    },

    /// The artifact, or the sidecar beside it, is not a plain file that could
    /// have been published here.
    ///
    /// Raised by a verification that found a directory, a symbolic link, a
    /// device, a file with more than one hard link or a file another uid owns
    /// wearing the name of an artifact. Never followed and never read:
    /// `list_backups` names the same case as
    /// [`UnreadableReason::NotARegularFile`](crate::backup::UnreadableReason::NotARegularFile).
    /// The variant carries nothing, because what it would say is a path.
    #[error("the artifact or its sidecar is not a plain file owned by root")]
    ArtifactNotRegularFile,

    /// The sidecar beside the artifact is missing, unreadable, of a version this
    /// agent does not understand, describes itself as unreadable, or records a
    /// digest that is not 64 lowercase hexadecimal characters.
    ///
    /// A finding about the backup's record, not about the host: the record
    /// cannot be believed, so the artifact cannot be compared with it.
    #[error("the artifact's sidecar is missing or cannot be trusted")]
    SidecarUnreadable,

    /// The artifact weighs something other than what its sidecar recorded.
    ///
    /// Checked beside the digest and after it. An artifact that was cut or
    /// extended hashes differently and is [`Self::ChecksumMismatch`]; this fires
    /// when the digest matched and only the recorded size disagrees, which is a
    /// sidecar that was edited or written wrong.
    #[error("the artifact's size does not match the one recorded for it")]
    ArtifactSizeMismatch,

    /// The manifest inside the archive names a different backup from the one
    /// that was asked for.
    ///
    /// An artifact copied or renamed under another id is refused rather than
    /// verified as whatever it contains: a verification reports counts and a
    /// size, and answering for an artifact the caller did not name would let a
    /// caller learn what another backup holds.
    #[error("the archive's manifest belongs to a different backup")]
    ManifestBackupMismatch,

    /// The manifest inside the archive cannot be read as a manifest by a
    /// verification: its JSON does not parse, or it names a database that no
    /// account could own.
    ///
    /// Separate from [`Self::ManifestUnwritable`] because the two are different
    /// facts that used to share a name: that variant says THIS AGENT could not
    /// write a manifest while creating a backup (a host failure), while this one
    /// says a manifest already on disk, inside an artifact whose digest matched,
    /// is not one this agent could have written (a finding about the artifact).
    /// Carries nothing: a name read out of the manifest is archive content.
    #[error("the archive's manifest cannot be read as a manifest")]
    ManifestUnreadable,

    /// The manifest inside the archive is larger than any manifest this agent
    /// writes.
    ///
    /// Refused before it is parsed, because a manifest is read whole into the
    /// memory of a root process and the extraction that produced it is the one
    /// read of archive CONTENT a verification makes.
    #[error("the archive's manifest is larger than this agent will read")]
    ManifestTooLarge,

    /// An archive member whose content root would read — the manifest — came out
    /// of the extraction as something other than a plain file with one link.
    ///
    /// A symbolic link named `manifest.json` that points at a file root can read
    /// is the dangerous case, and the layout scan does not catch it because it
    /// decides on names. Refused at the point of reading, before a byte of the
    /// target is followed.
    #[error("an archive member that must be a plain file is a link or another kind of entry")]
    MemberNotRegularFile,

    /// The manifest names a database whose dump is not among the archive's
    /// members.
    ///
    /// Carries no name: the manifest is archive content, and which database it
    /// was is not something the wire or an alert needs in order to act.
    #[error("a database the manifest names has no dump in the archive")]
    DumpMissing,

    /// The artifact is sealed and the call carried no key to open it with.
    ///
    /// Nothing was decrypted and nothing was changed. The panel reads it as
    /// "the destination this backup lives in has lost, or never sent, its key".
    /// On the wire it travels with [`BackupError::ArtifactDecryptionFailed`] as
    /// `ARCHIVE_UNOPENABLE` (issue #445): the operator's remedy is the same, to
    /// look at the destination's key, and neither is a bad request.
    #[error("the archive is encrypted and no key was supplied")]
    EncryptionKeyRequired,

    /// A sealed artifact cannot be opened with the key it was given.
    ///
    /// Named for what is known and not for what is likely. A wrong key, a
    /// damaged file, a header this agent does not know, and an archive cut
    /// short or extended all end here, because an AEAD that told them apart
    /// would be telling a guesser which of its guesses came close; a tampered
    /// sealed archive and a wrong key are indistinguishable by construction.
    /// Where the file's digest was checked first (a restore and a
    /// verification both do), a flipped bit would already have been a
    /// checksum mismatch, so the key is the likelier cause, and that reading
    /// is the panel's to make and word, never this variant's to assert.
    /// Carries nothing, not even a position: which chunk failed is a fact
    /// about the ciphertext, and a restore that is refused has no use for it.
    #[error("the archive cannot be opened with the key supplied for its destination")]
    ArtifactDecryptionFailed,

    /// Sealing a finished archive failed.
    ///
    /// A fault of this host (a full disk, no randomness, an unreadable file),
    /// never of the request. The plain archive and the half-written sealed one
    /// are both removed, and no artifact is published.
    #[error("the archive could not be encrypted")]
    ArtifactEncryptionFailed,

    /// The signing key an export was handed is not an unencrypted PKCS#8 ECDSA
    /// P-384 private key.
    ///
    /// A refusal about the REQUEST, raised before the account's backup lock is
    /// taken or a file is opened. Carries nothing: not the key, not the parser's
    /// wording, which could describe the key's bytes.
    #[error("the package signing key is not an ECDSA P-384 private key")]
    PackageSigningKeyRejected,

    /// The signature over a package manifest does not verify, or the key or
    /// signature is not a well-formed ECDSA P-384 one.
    ///
    /// One variant for a flipped bit in the manifest, a flipped bit in the
    /// signature and a key of the wrong shape: telling them apart would tell a
    /// forger which part of the forgery came close.
    #[error("the package signature does not verify")]
    PackageSignatureInvalid,

    /// The public key in a package is not the one the operator confirmed.
    ///
    /// The signature may be perfectly valid: it proves only that the holder of
    /// the key signed, and the fingerprint comparison is what ties that holder
    /// to the server the operator thinks the package came from.
    #[error("the package was signed by a key other than the one confirmed")]
    PackageFingerprintMismatch,

    /// The package manifest is not a manifest this agent understands: not JSON
    /// of the expected shape, a duplicate or unknown field, a version it does
    /// not know, a name no account could have, or a figure outside its bounds.
    #[error("the package manifest cannot be read as a manifest")]
    PackageManifestMalformed,

    /// A system user of the account's name already exists on this host.
    ///
    /// An imported account keeps its system user name, because a restore
    /// accepts only an artifact whose manifest names the account it restores
    /// into (issue #127, decision D1); a name already taken is therefore a
    /// conflict, reported before anything is written.
    #[error("a system user of that name already exists on this host")]
    ImportAccountExists,

    /// A backup with the package's id is already published with another digest.
    ///
    /// Ids are minted by the exporting host and are unique there; meeting one
    /// here with different bytes means an id collision or a tampered package,
    /// and neither is resolved by overwriting.
    #[error("a different backup with that id is already published")]
    ImportBackupIdTaken,

    /// The package declares more bytes than this host will stage.
    #[error("the package declares {declared} bytes, over the {limit} byte ceiling")]
    ImportTooLarge {
        /// The size the signed manifest declares.
        declared: u64,
        /// The ceiling that applies: the smaller of the caller's and the agent's.
        limit: u64,
    },

    /// The stream carried a different number of bytes than the signed manifest
    /// declares: more (trailing bytes are refused) or fewer.
    #[error("the package stream does not carry the number of bytes the manifest declares")]
    ImportLengthMismatch,

    /// The stream's SHA-256 is not the one the signed manifest declares.
    #[error("the package stream does not hash to the digest the manifest declares")]
    ImportDigestMismatch,

    /// The client's stream ended abnormally before the package was complete:
    /// the sender disappeared, or the stream failed.
    #[error("the package stream was interrupted")]
    ImportInterrupted,

    /// The staged artifact is sealed (encrypted) and so unreadable by this host.
    ///
    /// Raised on export for a sealed backup, and on import for a stream whose
    /// bytes open with the sealed header. A refusal about the artifact; nothing
    /// was published.
    #[error("the archive is encrypted and cannot be moved between servers")]
    SealedArtifactNotTransferable,

    /// The consumer of an export stopped taking bytes.
    ///
    /// The client went away or stopped reading for longer than the sink's
    /// patience. Nothing was changed on this host by the export; the lock is
    /// released.
    #[error("the export was stopped because the receiver stopped reading")]
    ExportStopped,

    /// A program this area started ran past its deadline, or wrote more output
    /// than the area is willing to hold, and was killed together with its
    /// process group.
    ///
    /// It states one thing only: the program was stopped by the agent. It is
    /// NOT a verdict on the archive, the dump or the database — a dump that was
    /// killed at its deadline has not been shown to be bad, and an archive
    /// listing that was cut off has not been shown to be hostile — so it never
    /// stands in for [`Self::DumpFailed`], [`Self::ArchiveFailed`],
    /// [`Self::DropFailed`] or [`Self::LoadFailed`], each of which says that
    /// the program ran and refused. The payload names the program's path and
    /// the bound and carries no byte the program printed.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),
}

#[cfg(test)]
#[path = "../tests/backup/backup_error_tests.rs"]
mod tests;
