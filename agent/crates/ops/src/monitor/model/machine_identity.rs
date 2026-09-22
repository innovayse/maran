//! What the panel found when it looked for this host's `machine-id`.

/// This host's `machine-id`, or the typed fact that none exists.
///
/// # Where it comes from
///
/// Read from `/etc/machine-id`
/// (`ProcessMonitorHost::read_machine_id`) —
/// the file `systemd-machine-id-setup(1)` documents as the canonical one.
/// `/var/lib/dbus/machine-id` is not read separately: on a real host it is a
/// symlink to `/etc/machine-id`, kept only for older D-Bus tools that do not
/// know the newer path (verified live on the machine this slice was written
/// on: `readlink -f /var/lib/dbus/machine-id` resolves to
/// `/etc/machine-id`), so reading it again would read the identical bytes
/// through a second path rather than a second source. Both supported
/// families ship systemd and place the file identically, so this is not a
/// `DistroAdapter` fact — see the constant's own doc comment in
/// `process_monitor_host.rs` for why it sits beside `/proc/stat` rather than
/// in the `distro` crate.
///
/// # Absence is a fact, not an error
///
/// A container with no systemd, or a chroot the installer never touched, has
/// no `/etc/machine-id` at all. [`Self::NotAvailable`] is what this type says
/// then — never an empty string, which this type structurally cannot
/// produce: [`Self::from_raw`] folds an empty or whitespace-only read into
/// `NotAvailable` rather than carrying it as `Present(String::new())`, so
/// nothing downstream can mistake "no machine-id" for "the machine-id
/// happens to be empty".
///
/// # What may be recorded
///
/// The [`Self::Present`] value identifies one physical or virtual machine for
/// as long as that machine exists. It travels over the gRPC channel to answer
/// `MonitorService.GetServerFingerprintInputs` and MUST NOT be written to any
/// log or audit journal verbatim (rules/security.md item 8; the threat note's
/// §4, "raw fingerprint inputs ... NEVER logged raw — only ...
/// equality-check outcome",
/// `docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md`).
/// Only a fact ABOUT it — present or absent, or later, a match/mismatch
/// verdict computed elsewhere — may be recorded.
#[derive(Debug, Clone, PartialEq, Eq)]
#[non_exhaustive]
pub enum MachineIdentity {
    /// The value `/etc/machine-id` held, trimmed of surrounding whitespace.
    Present(String),
    /// The file does not exist, or existed but was empty (or whitespace-only)
    /// once trimmed.
    NotAvailable,
}

impl MachineIdentity {
    /// Builds the typed answer from the host's raw, unvalidated read.
    ///
    /// A pure function over an `Option<String>`, like
    /// [`super::sftp_jail_status::SftpJailStatus::evaluate`] is over text: the
    /// whole question — does a usable machine-id exist — is answered without
    /// touching a filesystem, which is what lets a test decide the input
    /// directly instead of writing a real file.
    #[must_use]
    pub fn from_raw(raw: Option<String>) -> Self {
        match raw {
            Some(content) => {
                let trimmed = content.trim();
                if trimmed.is_empty() {
                    Self::NotAvailable
                } else {
                    Self::Present(trimmed.to_owned())
                }
            }
            None => Self::NotAvailable,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/monitor/machine_identity_tests.rs"]
mod tests;
