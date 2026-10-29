//! The programs and the bound the safe-write tests share.
//!
//! The protocol takes its programs as `AdapterBinary` values, a type only the
//! distribution crate can construct, so a test cannot write one as a literal. These
//! are a real adapter's values, built once, with the validator and reload shapes
//! every test of the protocol passes in. Mounted once from `safe_write/mod.rs`
//! under `#[cfg(test)]` rather than copied into each test file.

use std::sync::LazyLock;
use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{AdapterBinary, DistroFamily, adapter_for};

use crate::safe_write::model::{Reload, Validator};

/// The web server binary of the Debian-family adapter.
static NGINX: LazyLock<AdapterBinary> =
    LazyLock::new(|| adapter_for(DistroFamily::Debian).nginx_binary());

/// The service manager binary of the Debian-family adapter.
static SERVICE_MANAGER: LazyLock<AdapterBinary> =
    LazyLock::new(|| adapter_for(DistroFamily::Debian).service_manager());

/// The pure validator every protocol test passes in: `nginx -t`, in the short class.
pub(crate) fn validator() -> Validator<'static> {
    Validator {
        program: &NGINX,
        arguments: &["-t"],
        class: CallClass::Validate,
    }
}

/// The reload every protocol test passes in: `systemctl reload nginx`.
pub(crate) fn reload() -> Reload<'static> {
    Reload {
        program: &SERVICE_MANAGER,
        arguments: &["reload", "nginx"],
    }
}

/// A validator that, like the four mutating ones, asks the service manager.
pub(crate) fn mutating_validator() -> Validator<'static> {
    Validator {
        program: &SERVICE_MANAGER,
        arguments: &["daemon-reload"],
        class: CallClass::Reload,
    }
}

/// A timeout, as the bounded core reports it, for a program named `program`.
pub(crate) fn timed_out(program: &str) -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: program.to_owned(),
        deadline: Duration::from_secs(60),
    }
}
