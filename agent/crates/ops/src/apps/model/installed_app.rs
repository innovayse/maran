//! One app installed on this host.

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use crate::apps::model::app_kind::AppKind;
use crate::apps::model::app_service_status::AppServiceStatus;

/// One app installed on this host, as probed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstalledApp {
    /// The app.
    pub id: ValidatedAppId,
    /// What it is for.
    pub kind: AppKind,
    /// The version as the host reports it; empty when the host reports none.
    pub version: String,
    /// What the service manager says about its service.
    pub service_status: AppServiceStatus,
}
