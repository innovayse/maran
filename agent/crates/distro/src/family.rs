//! The supported distribution families.

/// Families the panel supports (spec §4).
///
/// A family, not a distribution: Ubuntu and Debian differ in release cadence and
/// package versions but agree on `apt`, unit names and file locations, which is
/// all the agent cares about. Adding Ubuntu 26.04 must not add a variant here.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DistroFamily {
    /// Ubuntu and Debian: apt, `nginx.service`, `/etc/nginx/sites-available`.
    Debian,
    /// AlmaLinux and Rocky: dnf, `nginx.service`, `/etc/nginx/conf.d`.
    Rhel,
}
