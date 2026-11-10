//! Values that name or describe filesystem entries: contained absolute paths,
//! account-relative paths, browse paths, search queries, permission modes.

pub mod archive_bounds_error;
pub mod archive_member_name;
pub mod archive_member_name_error;
pub mod browse_path;
pub mod browse_path_error;
pub mod create_bounds;
pub mod extract_bounds;
pub mod file_mode;
pub mod file_mode_error;
pub mod mutable_path;
pub mod mutable_path_error;
pub mod path;
pub(crate) mod path_component;
pub(crate) mod path_component_problem;
pub mod path_error;
pub mod relative_path;
pub mod relative_path_error;
pub mod remote_path;
pub mod remote_path_error;
pub mod search_query;
pub mod search_query_error;
pub mod transfer_bounds;
pub mod transfer_bounds_error;
pub mod trash_entry_id;
pub mod trash_entry_id_error;
pub mod upload_limit;
pub mod upload_limit_error;
