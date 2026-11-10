//! What the fixed probes established about a source.

use crate::transfer::model::source_database_server::SourceDatabaseServer;
use crate::transfer::model::source_disk::SourceDisk;
use crate::transfer::model::source_kind::SourceKind;
use crate::transfer::model::source_web_server::SourceWebServer;

/// The observations of one connection test. Every field is something the source
/// printed and a parser accepted, never an assumption: a probe that could not
/// run or whose answer did not parse leaves its field at the unknown value.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TestedSource {
    /// The login is root (effective uid 0).
    pub is_root: bool,
    /// The room under `/home`, or `None` when it could not be read.
    pub home_disk: Option<SourceDisk>,
    /// The `PRETTY_NAME` of the source's os-release file: bounded, printable,
    /// or `None`.
    pub system_release: Option<String>,
    /// Which adapter reads this source.
    pub kind: SourceKind,
    /// The version of the source's Maran panel; set only for
    /// [`SourceKind::Maran`].
    pub maran_version: Option<String>,
    /// The database server found, or `None`.
    pub database_server: Option<SourceDatabaseServer>,
    /// The web server found, or `None`.
    pub web_server: Option<SourceWebServer>,
    /// Whether a mail daemon or a virtual mail tree was found. A probe that
    /// could not run reads as `false`, which the wire cannot tell apart; mail is
    /// not migrated and the report warns when this is `true`.
    pub mail_found: bool,
}
