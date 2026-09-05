//! Values that become operating-system objects: users, groups, unit names, and
//! the lines of a crontab.

pub mod cron_command;
pub mod cron_command_error;
pub mod cron_entry_id;
pub mod cron_entry_id_error;
pub mod cron_schedule;
pub mod cron_schedule_error;
pub mod env_var_name;
pub mod env_var_name_error;
pub mod env_var_value;
pub mod env_var_value_error;
pub mod name;
pub mod name_error;
pub mod sftp_user_name;
pub mod sftp_user_name_error;
