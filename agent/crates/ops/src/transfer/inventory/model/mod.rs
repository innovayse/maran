//! The values an inventory read passes between its parsers, its steps and its
//! sink: plain data, every name a validated type, no behaviour beyond
//! construction and the bounds that belong to the value itself.

pub mod account_draft;
pub mod apache_virtual_host;
pub mod bounded_text;
pub mod claimed_names;
pub mod crontab_reading;
pub mod database_assignment;
pub mod inventory_limits;
pub mod inventory_sink;
pub mod inventory_step;
pub mod inventory_summary;
pub mod line_window;
pub mod listed_database;
pub mod nginx_server;
pub mod nginx_token;
pub mod parsed;
pub mod parsed_vhost;
pub mod passwd_entry;
pub mod site_assignment;
pub mod source_account_record;
pub mod source_database_record;
pub mod source_site_backend;
pub mod source_site_record;
pub mod source_warning_kind;
pub mod source_warning_record;
pub mod vhost_budget;
pub mod vhost_file;
