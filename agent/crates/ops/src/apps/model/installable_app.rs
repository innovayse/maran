//! One app this host can install.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use crate::apps::model::app_kind::AppKind;

/// One app of the catalogue that the host's package sources can install, or that is installed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallableApp {
    /// The app.
    pub id: ValidatedAppId,
    /// What it is for.
    pub kind: AppKind,
    /// Whether the app is already installed.
    pub installed: bool,
}
