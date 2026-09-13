//! The three TLS-version option names a rendered `vsftpd.conf` writes.

/// The names — not the values — of the three TLS protocol-version options the
/// agent writes into a rendered `vsftpd.conf`.
///
/// A family of options whose SPELLING differs between distributions, which is
/// what makes it a platform fact and not a constant: the Debian family's build
/// of vsftpd 3.0.5 knows `ssl_tlsv1`, `ssl_tlsv11`, `ssl_tlsv12`, while the
/// RHEL family's knows `ssl_tlsv1`, `ssl_tlsv1_1`, `ssl_tlsv1_2` (measured
/// 2026-09-09 on `ubuntu:24.04` with `vsftpd 3.0.5-0ubuntu3.1` and on
/// `almalinux:9` with `vsftpd 3.0.5-8.el9`, by `strings` on each shipped binary
/// and by running the daemon against a config carrying each spelling).
///
/// Every one of the three is carried, [`Self::tls_v1`] included, even though
/// both families spell that one identically today. Carrying only the two that
/// differ would leave the template holding one member of the family as a
/// literal and receiving its siblings as values, and the identical one is
/// exactly the trap: it is the one that reads as safe to type. A member is
/// added here when the template writes the key, so there is no `tls_v1_3` — the
/// template leaves TLS 1.3 at the build's own default, which enables it.
pub struct VsftpdTlsVersionKeys {
    /// The option name that switches TLS 1.0 on or off.
    ///
    /// `ssl_tlsv1` on both families today. It is answered by the adapter
    /// regardless, so no reader has to know which of the three is currently
    /// the same word everywhere.
    pub tls_v1: &'static str,
    /// The option name that switches TLS 1.1 on or off.
    ///
    /// `ssl_tlsv11` on the Debian family, `ssl_tlsv1_1` on the RHEL family.
    pub tls_v1_1: &'static str,
    /// The option name that switches TLS 1.2 on or off.
    ///
    /// `ssl_tlsv12` on the Debian family, `ssl_tlsv1_2` on the RHEL family.
    pub tls_v1_2: &'static str,
}
