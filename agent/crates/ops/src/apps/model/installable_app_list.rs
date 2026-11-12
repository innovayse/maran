//! The apps this host can install, and what could not be asked.

use crate::apps::model::installable_app::InstallableApp;
use crate::apps::model::nodejs_unavailable::NodejsUnavailable;

/// What `list_installable_apps` found: the apps, and why the Node.js ones are missing when they
/// are missing because the host could not say (rather than because it does not carry them).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallableAppList {
    /// The apps that can be installed, or are installed, in catalogue order.
    pub apps: Vec<InstallableApp>,
    /// `Some` when the Node.js module streams gave no offer; the Node.js apps that are INSTALLED
    /// are still in [`Self::apps`], the others are absent for this reason.
    pub nodejs_unavailable: Option<NodejsUnavailable>,
}
