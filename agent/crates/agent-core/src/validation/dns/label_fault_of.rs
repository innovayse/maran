//! The one label check a record name and a target share.

use crate::validation::dns::label_fault::LabelFault;

/// The longest label DNS allows.
const MAX_LABEL_LENGTH: usize = 63;

/// Returns what is wrong with `label`, or `None` when it is acceptable: 1 to 63
/// ASCII letters, digits, `-` and `_`, with no hyphen at either edge.
///
/// The underscore is allowed anywhere because service labels (`_sip`, `_tcp`,
/// `_dmarc`, `_domainkey`, `_acme-challenge`) are made of it. Uppercase passes;
/// the caller lowercases.
pub(crate) fn label_fault_of(label: &str) -> Option<LabelFault> {
    if label.is_empty() {
        return Some(LabelFault::Empty);
    }

    if label.len() > MAX_LABEL_LENGTH {
        return Some(LabelFault::TooLong);
    }

    if let Some(character) = label
        .chars()
        .find(|c| !c.is_ascii_alphanumeric() && *c != '-' && *c != '_')
    {
        return Some(LabelFault::IllegalCharacter(character));
    }

    if label.starts_with('-') || label.ends_with('-') {
        return Some(LabelFault::HyphenAtEdge);
    }

    None
}
