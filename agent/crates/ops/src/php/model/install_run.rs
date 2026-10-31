//! One finished run of the PHP install unit, as the service manager reports it.

/// The property the service manager names a unit's latest run by.
const INVOCATION_ID: &str = "InvocationID=";

/// The property holding the exit status of the unit's main process.
const EXEC_MAIN_STATUS: &str = "ExecMainStatus=";

/// The length of an invocation id: 128 bits written as lowercase hexadecimal.
const INVOCATION_ID_LENGTH: usize = 32;

/// Which run of the install unit failed, and what its package manager exited
/// with — the two facts the package-failure classification reads.
///
/// Built only by [`Self::parse`], so a value that exists names one run: the
/// invocation id is 32 lowercase hexadecimal characters and nothing else,
/// which is what lets it be handed to the journal reader as one argv element
/// without any reading of its own (rules/security.md item 3), and the exit
/// status is a number the service manager stated rather than one assumed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallRun {
    /// The service manager's id of the run, which every journal line the
    /// run's processes wrote carries as `_SYSTEMD_INVOCATION_ID`.
    invocation_id: String,
    /// The exit status of the unit's main process — the package manager.
    exit_status: i32,
}

impl InstallRun {
    /// Reads the answer of `systemctl show --property=InvocationID
    /// --property=ExecMainStatus <unit>`: one `Name=value` line per property,
    /// in whatever order the service manager prints them.
    ///
    /// Returns `None` when either property is missing, when the invocation id
    /// is not 32 lowercase hexadecimal characters — the service manager prints
    /// an empty one for a unit that has never run, which is also what a unit
    /// that does not exist answers — or when the status is not a number.
    #[must_use]
    pub fn parse(show: &str) -> Option<Self> {
        let mut invocation_id = None;
        let mut exit_status = None;
        for line in show.lines() {
            if let Some(value) = line.strip_prefix(INVOCATION_ID) {
                invocation_id = Some(value.trim());
            } else if let Some(value) = line.strip_prefix(EXEC_MAIN_STATUS) {
                exit_status = Some(value.trim());
            }
        }

        let invocation_id = invocation_id.filter(|id| {
            id.len() == INVOCATION_ID_LENGTH
                && id
                    .bytes()
                    .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
        })?;
        let exit_status = exit_status?.parse().ok()?;

        Some(Self {
            invocation_id: invocation_id.to_owned(),
            exit_status,
        })
    }

    /// The service manager's id of the run.
    #[must_use]
    pub fn invocation_id(&self) -> &str {
        &self.invocation_id
    }

    /// The exit status the package manager ended the run with.
    #[must_use]
    pub fn exit_status(&self) -> i32 {
        self.exit_status
    }
}

#[cfg(test)]
#[path = "../../tests/php/model/install_run_tests.rs"]
mod tests;
