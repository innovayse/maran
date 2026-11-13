//! The app store of the host: the web servers and language runtimes the panel installs,
//! removes and controls (issue #517, `docs/apps/README.md`).
//!
//! An "app" is an operating-system package set the panel runs for sites. The catalogue is
//! CLOSED and owned here: `nginx`, `apache`, `php-<version>` and `nodejs-<major>`. The panel
//! names an app by id and never by package, so the agent's package list is never the caller's
//! choice (rules/security.md item 12); the id is parsed into a `ValidatedAppId` at the service
//! boundary and every operation here asks `app_catalogue::ensure_in_catalogue` before it uses it.
//!
//! The package manager never installs or removes anything as the agent's child: that runs in the
//! installer's root units (`maran-app-install@`, `maran-app-remove@`), started through the shared
//! [`crate::packages`] machinery. The one thing the agent runs itself is the package manager's
//! DRY RUN of a removal, before the removal unit starts (`removal_plan`). What an app IS on the host is PROBED, never remembered: the
//! files a package leaves and the service manager's own answer, so an app installed or removed
//! by hand is reported as it is.
//!
//! The panel's own front is nginx on 8443. That nginx is infrastructure, not an app, and its
//! package is never removed: uninstalling the `nginx` app only checks that no site is served by
//! it, and stopping it is refused because it would take the panel off the air.

mod apache_is_the_site_web_server;
mod app_catalogue;
mod app_in_use;
mod app_packages;
mod apps_host;
mod apps_op_error;
mod control_app_service;
mod ensure_node_is_managed;
mod hold_apache_off;
mod install_app;
mod installed_node_major;
mod installed_probe;
mod installed_site_web_server;
mod list_apps;
mod list_installable_apps;
mod marker_text;
pub mod model;
mod nginx_port80;
mod node_origin;
mod nodejs_module_operation;
mod offered_nodejs_streams;
mod parse_marker;
mod prepare_apache;
mod process_apps_host;
mod removal_plan;
mod service_status;
mod uninstall_app;

pub use apache_is_the_site_web_server::apache_is_the_site_web_server;
pub use apps_host::AppsHost;
pub use apps_op_error::AppsOpError;
pub use control_app_service::control_app_service;
pub use install_app::install_app;
pub use installed_site_web_server::installed_site_web_server;
pub use list_apps::list_apps;
pub use list_installable_apps::list_installable_apps;
pub use model::app_kind::AppKind;
pub use model::app_service_action::AppServiceAction;
pub use model::app_service_status::AppServiceStatus;
pub use model::installable_app::InstallableApp;
pub use model::installable_app_list::InstallableAppList;
pub use model::installed_app::InstalledApp;
pub use model::nodejs_unavailable::NodejsUnavailable;
pub use uninstall_app::uninstall_app;
