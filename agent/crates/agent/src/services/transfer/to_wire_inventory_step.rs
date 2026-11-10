//! An inventory step onto its wire enum.

use maran_ops::transfer::inventory::model::inventory_step::InventoryStep;

use crate::proto::SourceInventoryStep;

/// The wire step for a step of the read. Exhaustive on purpose: a step added to
/// the operation must name its wire member here before this builds.
#[must_use]
pub fn to_wire_inventory_step(step: InventoryStep) -> SourceInventoryStep {
    match step {
        InventoryStep::Accounts => SourceInventoryStep::Accounts,
        InventoryStep::Sites => SourceInventoryStep::Sites,
        InventoryStep::Databases => SourceInventoryStep::Databases,
        InventoryStep::Cron => SourceInventoryStep::Cron,
        InventoryStep::Disk => SourceInventoryStep::Disk,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_inventory_step_tests.rs"]
mod tests;
