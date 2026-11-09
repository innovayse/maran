//! PurgeExpiredTrash hands the request to the host and returns the count.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::num::NonZeroU32;

use maran_agent_core::validation::system::name::AccountName;

use super::purge_expired_trash;
use crate::files::FilesOpError;
use crate::files::fake_files_host::{Call, FakeFilesHost};
use crate::files::model::purge_expired_trash_input::PurgeExpiredTrashInput;

fn input() -> PurgeExpiredTrashInput {
    PurgeExpiredTrashInput {
        account: AccountName::parse("alice").unwrap(),
        older_than_days: NonZeroU32::new(30).unwrap(),
    }
}

#[test]
fn the_count_of_the_host_is_returned() {
    let host = FakeFilesHost::new();

    assert_eq!(purge_expired_trash(&host, &input()), Ok(4));
    assert_eq!(host.calls(), vec![Call::PurgeExpiredTrash]);
}

#[test]
fn the_bulk_bound_comes_back_as_saturated() {
    let host = FakeFilesHost::failing_mutation(FilesOpError::Saturated);

    assert_eq!(
        purge_expired_trash(&host, &input()),
        Err(FilesOpError::Saturated)
    );
}
