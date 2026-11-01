//! Values that become operating-system objects: users, groups, unit names, and
//! the lines of a crontab, the ceilings written onto an account's systemd slice
//! (`cpu_quota_percent`, `memory_max_mib`, `tasks_max`) — and the proof that an existing host user is one of
//! the panel's hosting accounts (`managed_account`).

pub mod backup_id;
pub mod backup_id_error;
pub mod cpu_quota_percent;
pub mod cpu_quota_percent_error;
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
pub mod ftps_user_name;
pub mod ftps_user_name_error;
pub mod local_backup_root;
pub mod local_backup_root_error;
pub mod managed_account;
pub mod managed_account_error;
pub mod memory_max_mib;
pub mod memory_max_mib_error;
pub mod name;
pub mod name_error;
pub mod sftp_user_name;
pub mod sftp_user_name_error;
pub mod ssh_public_key;
pub mod ssh_public_key_error;
pub mod tasks_max;
pub mod tasks_max_error;
