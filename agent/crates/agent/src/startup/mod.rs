//! What the daemon does before it serves: binding the socket and reconciling the
//! trees a killed process can leave half written.
//!
//! Every pass here runs BEFORE the first connection is accepted, which is the
//! whole safety argument for each of them (see `server::serve`).

pub(crate) mod bind_socket;
pub(crate) mod reconcile_dns_zones;
pub(crate) mod reconcile_import_users;
pub(crate) mod reconcile_opcache_drop_ins;
pub(crate) mod reconcile_php_pool_trees;
pub(crate) mod reconcile_sftp_key_files;
pub(crate) mod reconcile_site_auth_files;
pub(crate) mod reconcile_web_tree;
pub(crate) mod web_tree_reconciliation;
