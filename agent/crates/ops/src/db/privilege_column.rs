//! Where `mysql.db` keeps each privilege.

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;

/// The column of `mysql.db` that records whether a row holds the grant option.
///
/// Read so that it can be revoked; it is never granted, and no
/// [`DatabasePrivilege`] stands for it.
pub(crate) const GRANT_OPTION_COLUMN: &str = "Grant_priv";

/// The `mysql.db` column that records `privilege`.
///
/// The server's grant table has one `Y`/`N` column per privilege, and reading them
/// by name is the only way to learn what a row holds without parsing rendered
/// `SHOW GRANTS` text. Named columns, never positions: the table's column order
/// differs between server majors.
pub(crate) const fn privilege_column(privilege: DatabasePrivilege) -> &'static str {
    match privilege {
        DatabasePrivilege::Select => "Select_priv",
        DatabasePrivilege::Insert => "Insert_priv",
        DatabasePrivilege::Update => "Update_priv",
        DatabasePrivilege::Delete => "Delete_priv",
        DatabasePrivilege::Create => "Create_priv",
        DatabasePrivilege::Drop => "Drop_priv",
        DatabasePrivilege::Alter => "Alter_priv",
        DatabasePrivilege::Index => "Index_priv",
        DatabasePrivilege::References => "References_priv",
        DatabasePrivilege::CreateTemporaryTables => "Create_tmp_table_priv",
        DatabasePrivilege::LockTables => "Lock_tables_priv",
        DatabasePrivilege::CreateView => "Create_view_priv",
        DatabasePrivilege::ShowView => "Show_view_priv",
        DatabasePrivilege::CreateRoutine => "Create_routine_priv",
        DatabasePrivilege::AlterRoutine => "Alter_routine_priv",
        DatabasePrivilege::Execute => "Execute_priv",
        DatabasePrivilege::Trigger => "Trigger_priv",
        DatabasePrivilege::Event => "Event_priv",
    }
}
