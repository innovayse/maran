//! What a restore's verification decided.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::model::dump_origin::DumpOrigin;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::restore_files_plan::RestoreFilesPlan;

/// What verification decided: which databases to replace, and from which
/// manifest entry.
///
/// Built only by `prepare_restore`, which is the only thing that has checked any of
/// it. A value of this type means every refusal in steps 1 to 5 has already
/// been made — which is what lets `perform` be read as "and now the
/// irreversible half", with no verification mixed into it.
pub(super) struct RestorePlan {
    /// Every database to replace, paired with the manifest entry that
    /// describes its dump, in the manifest's own order. Every database the
    /// manifest names for a whole restore, exactly one for a single-database
    /// restore and none for a single-directory one.
    pub(super) databases: Vec<(DatabaseName, ManifestDatabase)>,

    /// What happens to the account's files.
    pub(super) files: RestoreFilesPlan,

    /// Whose hand wrote the dumps in `databases`, which decides how each is
    /// loaded: the superuser for this host's own, the restricted loader for
    /// an imported artifact's. Read once from the sidecar, by the same
    /// function that checks the sidecar against the manifest, so no later step
    /// decides it again.
    pub(super) origin: DumpOrigin,
}
