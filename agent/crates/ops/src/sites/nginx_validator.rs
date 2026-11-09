//! The check every write to the nginx tree runs: `nginx -t`.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::model::Validator;
use crate::sites::write_vhost::VALIDATE_ARGUMENT;

/// Builds the validator that asks nginx whether the whole tree loads.
///
/// One place, because four writers need it — a vhost written, created or
/// removed, and a credential file written beside them — and four hand-kept
/// copies of "which program, which argument, which deadline" is how one of them
/// ends up checking with a different tool. It is a pure check, so the short
/// [`CallClass::Validate`]: `nginx -t` reads the tree and changes nothing.
///
/// `nginx` is the adapter's binary: the path differs between families and an
/// operation that wrote it as a literal would be guessing (rules/rust.md
/// "Distro adapter").
pub(crate) fn nginx_validator(nginx: &AdapterBinary) -> Validator<'_> {
    Validator {
        program: nginx,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    }
}
