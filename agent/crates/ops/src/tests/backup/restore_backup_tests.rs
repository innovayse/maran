//! A restore, and what the account is if it stops halfway.
//!
//! The cases are split by subject, one file each under `restore_backup/`:
//! the whole restore, its rollback, the scratch chain it stages in, the
//! account identity it re-asks, a restore of one database, a restore of one
//! directory, a sealed archive and an imported archive. What they share is
//! `restore_fixture`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "restore_backup/account_identity_tests.rs"]
mod account_identity_tests;
#[path = "restore_backup/database_scope_tests.rs"]
mod database_scope_tests;
#[path = "restore_backup/encrypted_restore_tests.rs"]
mod encrypted_restore_tests;
#[path = "restore_backup/imported_restore_tests.rs"]
mod imported_restore_tests;
#[path = "restore_backup/restore_fixture.rs"]
mod restore_fixture;
#[path = "restore_backup/rollback_tests.rs"]
mod rollback_tests;
#[path = "restore_backup/scratch_chain_tests.rs"]
mod scratch_chain_tests;
#[path = "restore_backup/site_scope_tests.rs"]
mod site_scope_tests;
#[path = "restore_backup/whole_restore_tests.rs"]
mod whole_restore_tests;
