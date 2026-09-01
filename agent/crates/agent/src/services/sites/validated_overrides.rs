//! Revalidating the customer's php.ini overrides against the agent's own list.

use maran_ops::php::PhpOverride;

use crate::proto::{AgentError, PhpSetting};
use crate::services::sites::invalid_input::invalid_input;

/// Revalidates the customer's php.ini overrides against the agent's own
/// whitelist.
///
/// The panel keeps a copy of the whitelist so a customer sees a refusal before
/// they save. This is the agent's copy, and it is the one that decides
/// (rules/security.md item 1): the panel is another network peer, and a bug
/// there must not become a `php_value` line the agent writes as root.
///
/// # Errors
///
/// Returns the wire error for a setting that is not on the whitelist, or a
/// value that is malformed, out of range, or carries a control character.
pub fn validated_overrides(settings: &[PhpSetting]) -> Result<Vec<PhpOverride>, AgentError> {
    settings
        .iter()
        .map(|setting| {
            PhpOverride::parse(&setting.name, &setting.value)
                .map_err(|error| invalid_input(error.to_string()))
        })
        .collect()
}
