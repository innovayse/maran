//! What the machine identity looks like on the wire.

use maran_ops::monitor::MachineIdentity;

use crate::proto::MachineIdStatus;

/// Converts the agent's own [`MachineIdentity`] onto its wire shape.
///
/// `MachineIdentity` is `#[non_exhaustive]`: a wildcard reports `present:
/// false` for a future variant this match does not yet know, the same
/// "unspecified/absent" fallback every other conversion in this folder uses,
/// rather than a broken build.
#[must_use]
pub fn reported_machine_id(identity: MachineIdentity) -> MachineIdStatus {
    match identity {
        MachineIdentity::Present(value) => MachineIdStatus {
            present: true,
            value,
        },
        _ => MachineIdStatus {
            present: false,
            value: String::new(),
        },
    }
}
