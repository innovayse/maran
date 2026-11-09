//! One extension reading from `ops` onto its wire payload.

use maran_ops::php::{ExtensionAvailability, PhpExtension};

use crate::proto::{PhpExtension as ProtoExtension, PhpExtensionAvailability};

/// Builds the wire entry for one catalogue extension.
///
/// The availability is mapped one to one and never to `Unspecified`: the
/// agent always knows what its own adapter answered, so an unspecified value on
/// the wire means only "an older agent" to the panel.
#[must_use]
pub fn reported_php_extension(extension: &PhpExtension) -> ProtoExtension {
    let availability = match extension.availability {
        ExtensionAvailability::Available => PhpExtensionAvailability::Available,
        ExtensionAvailability::DisableNotSupported => PhpExtensionAvailability::DisableNotSupported,
        ExtensionAvailability::NotPackaged => PhpExtensionAvailability::NotPackaged,
    };

    ProtoExtension {
        name: extension.name.as_str().to_owned(),
        enabled: extension.enabled,
        availability: availability as i32,
    }
}

#[cfg(test)]
#[path = "../../tests/services/php/reported_php_extension_tests.rs"]
mod tests;
