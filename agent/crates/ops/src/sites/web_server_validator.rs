//! The check every write to a web server's tree runs: its own `-t`.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::safe_write::model::Validator;
use crate::sites::write_vhost::VALIDATE_ARGUMENT;

/// Builds the validator that asks the site web server whether the whole tree loads.
///
/// One place, because four writers need it — a vhost written, created or
/// removed, and a credential file written beside them — and four hand-kept
/// copies of "which program, which argument, which deadline" is how one of them
/// ends up checking with a different tool. It is a pure check, so the short
/// [`CallClass::Validate`]: `nginx -t` and Apache's `apachectl -t` both read the
/// tree and change nothing, and both take the same single argument.
///
/// `binary` is the adapter's binary for the site web server in use
/// ([`maran_distro::WebServerFacts::site_web_server_binary`]): the path differs
/// between servers and between families, and an operation that wrote it as a
/// literal would be guessing (rules/rust.md "Distro adapter").
pub(crate) fn web_server_validator(binary: &AdapterBinary) -> Validator<'_> {
    Validator {
        program: binary,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    }
}
