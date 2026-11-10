//! Tests for the inventory step on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::inventory::model::inventory_step::InventoryStep;

use super::to_wire_inventory_step;
use crate::proto::SourceInventoryStep;

#[test]
fn every_step_has_its_own_wire_member() {
    for (step, expected) in [
        (InventoryStep::Accounts, SourceInventoryStep::Accounts),
        (InventoryStep::Sites, SourceInventoryStep::Sites),
        (InventoryStep::Databases, SourceInventoryStep::Databases),
        (InventoryStep::Cron, SourceInventoryStep::Cron),
        (InventoryStep::Disk, SourceInventoryStep::Disk),
    ] {
        assert_eq!(to_wire_inventory_step(step), expected);
        assert_ne!(
            to_wire_inventory_step(step),
            SourceInventoryStep::Unspecified
        );
    }
}
