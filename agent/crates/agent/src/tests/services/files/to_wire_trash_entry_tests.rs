//! Tests for `to_wire_trash_entry`: every field of a trashed item reaches the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::trashed_entry::TrashedEntry as TrashedItem;

use super::to_wire_trash_entry;

#[test]
fn a_trashed_item_keeps_every_field_on_the_wire() {
    let wire = to_wire_trash_entry(TrashedItem {
        trash_id: "1700000000-0123456789abcdef".to_owned(),
        original_path: "sites/old".to_owned(),
        trashed_at_unix: 1_700_000_000,
        is_directory: true,
        size_bytes: 42,
    });

    assert_eq!(wire.trash_id, "1700000000-0123456789abcdef");
    assert_eq!(wire.original_path, "sites/old");
    assert_eq!(wire.trashed_at_unix, 1_700_000_000);
    assert!(wire.is_directory);
    assert_eq!(wire.size_bytes, 42);
}
