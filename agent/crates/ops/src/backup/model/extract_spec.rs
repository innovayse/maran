//! Everything one extraction of an archive is told, as one value.

use std::path::{Path, PathBuf};

use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_identity::ExtractIdentity;

/// The flag that makes `tar` read an archive out.
const EXTRACT: &str = "--extract";

/// The flag that names the decompressor, by absolute path, matching the create
/// side.
///
/// The mirror of
/// [`ArchiveSpec`](crate::backup::model::archive_spec::ArchiveSpec)'s own
/// constant, and for the same reason: `--gzip` makes `tar` resolve a bare
/// `gzip` through `PATH`, in a child of a root daemon whose unit pins no `PATH`.
/// A restore is the half where the input is a customer-supplied archive, so it
/// is the LAST place the program reading that archive should be chosen by an
/// environment variable.
///
/// One measured difference from the create side, worth knowing when reading a
/// transcript: on the read side GNU tar spawns the named program directly —
/// `execve(<the named program>, [<the named program>, "-d"])`, no shell at all —
/// so restore becomes entirely shell-free, while creation keeps tar's own
/// internal `sh -c` around a constant absolute path.
const COMPRESS_PROGRAM: &str = "--use-compress-program=";

/// The flag naming the archive file to read.
const FILE: &str = "--file";

/// The archive `tar` is told to read: its own standard input, never a path.
///
/// **This is a security control, not a style.** The home half of an extraction
/// runs inside `fork_as_account`, i.e. as the customer, while the artifact it
/// must read is `0600` inside a `0700` root-owned directory — a property this
/// product asserts on a real host and intends to keep. A path argument cannot
/// satisfy both: the customer's `tar` cannot open that file, and the only way
/// to make it able to would be to loosen the artifact, trading a proven
/// property for a convenience.
///
/// A descriptor satisfies both. Root — which may open the artifact — opens it
/// and hands the OPEN FILE to the child, and the child reads bytes it could
/// never have opened itself. The same shape the database side already uses when
/// it feeds a dump to the client (`ProcessBackupHost::load_dump`).
///
/// Naming the artifact here as well would be worse than redundant: it would be
/// a second, weaker way in that a future edit could start relying on.
const STANDARD_INPUT: &str = "-";

/// The flag that changes directory before extracting.
const CHANGE_DIRECTORY: &str = "-C";

/// The flag that reads uid/gid as NUMBERS rather than looking names up.
///
/// The mirror of the create side's own `--numeric-owner`, and here it also
/// keeps the extraction from consulting this host's `/etc/passwd` for names an
/// archive supplied.
const NUMERIC_OWNER: &str = "--numeric-owner";

/// The flag that refuses to take ownership from the archive.
///
/// Stated on BOTH extractions rather than left to whatever the effective uid
/// implies. The root-side one would otherwise honour the uid/gid stored in a
/// customer-supplied archive and write files into the scratch owned by anybody
/// it named; the account-side one is already unprivileged, so there the flag
/// costs nothing and says the intent out loud.
const NO_SAME_OWNER: &str = "--no-same-owner";

/// The flag that refuses to take permission bits from the archive.
///
/// What it costs is stated rather than hidden: setuid and setgid bits inside a
/// restored home are NOT restored, and ordinary read/write/execute bits come
/// back masked by the extracting process's umask. What it buys is that no
/// member of a customer-supplied archive can decide its own mode while root is
/// unpacking the `databases/` half — the half whose files a superuser loader
/// then reads.
const NO_SAME_PERMISSIONS: &str = "--no-same-permissions";

/// The flag that drops leading path components as members are extracted.
///
/// **This is the mirror of the create side's transform, and it is deliberately
/// NOT a transform.** The archive stores the home under a `home/` prefix, and
/// that prefix has to come off; `--transform` would do it, and `--transform`
/// rewrites the targets of relative symlinks unless it carries the `S` flag —
/// the silent corruption the create side measured and had to disable. Rather
/// than rely on remembering that flag a second time, extraction strips a
/// component, which touches member NAMES only and cannot reach a symlink's
/// target at all. The safe answer here is a different mechanism, not the same
/// mechanism used carefully.
const STRIP_ONE_COMPONENT: &str = "--strip-components=1";

/// The archive's manifest member. Part of the fixed layout (R2).
const MANIFEST_MEMBER: &str = "manifest.json";

/// The archive's directory of SQL dumps.
const DATABASES_MEMBER: &str = "databases";

/// The archive's copy of the account's home.
const HOME_MEMBER: &str = "home";

/// What one extraction is asked to do, as data rather than as an argv a caller
/// assembled.
///
/// The argv is built by [`ExtractSpec::arguments`] and by nothing else, which
/// is what makes "no extraction ever passes `--absolute-names`" a property one
/// unit test can state instead of a property every call site has to be read
/// for.
///
/// **There is no `-P`/`--absolute-names`, and there never will be.** Without it
/// `tar` strips a leading `/` and refuses a member containing a `..` component,
/// which is the second of the two things standing between a hostile archive and
/// `/etc/cron.d`. The first is the pre-scan, which refuses such a member before
/// this argv is built at all; this struct has no field that could turn the
/// second one off.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExtractSpec {
    /// The `.tar.gz` to read. Always a path this agent built.
    ///
    /// **The path the HOST opens, and never an argument the archiver is
    /// given.** [`Self::arguments`] names standard input instead — see
    /// `STANDARD_INPUT` — so this field says WHICH artifact is to be read and
    /// says nothing about who may read it. The host opens it as root, before
    /// any drop of privilege.
    pub artifact: PathBuf,

    /// The directory to extract into — the root-only scratch for
    /// [`ArchivePart::Manifest`] and [`ArchivePart::Databases`], and the
    /// restore's staging directory for [`ArchivePart::Home`].
    pub into: PathBuf,

    /// Which member of the archive this run extracts.
    pub part: ArchivePart,
}

impl ExtractSpec {
    /// Who this extraction must run as.
    ///
    /// **Derived from [`Self::part`], so the identity cannot disagree with the
    /// member.** R3 and R4 are one rule with two halves — the dumps are read by
    /// root because the loader connects as the database superuser and a dump
    /// file sitting in account-writable space can be swapped between extraction
    /// and load; the home is read by the account because that turns "root
    /// writes wherever the archive says" into "the account writes where the
    /// account already could". A spec with separate `member` and `run_as`
    /// fields would let one call site pair them the wrong way round, and the
    /// wrong pairing is root unpacking a customer's archive into a customer's
    /// home. Two fields would be two chances; one derivation is none.
    #[must_use]
    pub fn identity(&self) -> ExtractIdentity {
        match &self.part {
            ArchivePart::Manifest | ArchivePart::Databases => ExtractIdentity::Root,
            ArchivePart::Home { account } => ExtractIdentity::Account(account.clone()),
        }
    }

    /// The complete argv, program excluded, in the order `tar` reads it.
    ///
    /// **The artifact is NOT in this argv.** `--file -` names the archiver's own
    /// standard input, and the host opens the artifact as root and hands the
    /// descriptor to the process that runs — see `STANDARD_INPUT` for why a
    /// path there cannot be made to work. [`Self::artifact`] is therefore read
    /// by the host and by nothing else.
    ///
    /// Owned `String`s for the reason the create side's builder uses them: one
    /// element is a path that must become text somewhere, and doing it once
    /// here keeps the lossy conversion in one place. Every path in this spec is
    /// agent-built from a validated account name and a validated backup id, so
    /// the lossy branch is unreachable; a `tar` that then fails to open a
    /// mangled name is a failed restore rather than a restore of the wrong
    /// thing.
    ///
    /// `compressor` is the absolute path of `gzip`, supplied by the host that
    /// holds the adapter's answer — see
    /// [`ArchiveSpec::arguments`](crate::backup::model::archive_spec::ArchiveSpec::arguments)
    /// for why it is a parameter rather than a field on the spec.
    #[must_use]
    pub fn arguments(&self, compressor: &str) -> Vec<String> {
        let mut arguments = vec![
            EXTRACT.to_owned(),
            format!("{COMPRESS_PROGRAM}{compressor}"),
            NUMERIC_OWNER.to_owned(),
            NO_SAME_OWNER.to_owned(),
            NO_SAME_PERMISSIONS.to_owned(),
            FILE.to_owned(),
            STANDARD_INPUT.to_owned(),
        ];

        if matches!(self.part, ArchivePart::Home { .. }) {
            arguments.push(STRIP_ONE_COMPONENT.to_owned());
        }

        arguments.push(CHANGE_DIRECTORY.to_owned());
        arguments.push(text(&self.into));
        arguments.push(self.member().to_owned());

        arguments
    }

    /// The archive member name this part names.
    #[must_use]
    pub fn member(&self) -> &'static str {
        match self.part {
            ArchivePart::Manifest => MANIFEST_MEMBER,
            ArchivePart::Databases => DATABASES_MEMBER,
            ArchivePart::Home { .. } => HOME_MEMBER,
        }
    }
}

/// A path as text, lossily — see [`ExtractSpec::arguments`] for why lossily is
/// the right answer here.
fn text(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

#[cfg(test)]
#[path = "../../tests/backup/extract_spec_tests.rs"]
mod tests;
