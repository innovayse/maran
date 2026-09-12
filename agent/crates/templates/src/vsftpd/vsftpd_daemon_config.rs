//! The whole `vsftpd.conf` the agent writes for the FTPS daemon.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete `vsftpd.conf` — the host's one FTPS daemon
/// configuration, written whole on every apply.
///
/// **Whole, never merged into.** vsftpd's parser takes the **LAST** occurrence
/// of a key, measured with controls on both families on 2026-09-09: a
/// duplicated `listen_port` binds the appended value, and a duplicated
/// `force_local_logins_ssl` behaves as the appended one in both directions. So
/// an append is not an inert line — an appended `force_local_logins_ssl=NO`
/// silently switches off the forced TLS this whole daemon exists to enforce, on
/// a file that still parses and still starts. The file therefore goes through
/// `ops::safe_write`, which replaces it; nothing anywhere merges a key into an
/// existing `vsftpd.conf`, and the daemon's `-o` command-line settings are not
/// an exception to that rule but the ordinary case of it — they are applied
/// after the file is read, which under last-wins is the rule itself.
///
/// **The render ends with a newline, and the template carries a comment to keep
/// it there.** askama drops one trailing newline from a template, so the render
/// ended `require_ssl_reuse=NO` with nothing after it. A line appended to such a
/// file fuses with the last key into `require_ssl_reuse=NOforce_local_logins_ssl=NO`
/// — one unrecognised variable, which the RHEL family reports as `500 OOPS:
/// unrecognised variable in config file` and on which the Debian family's build
/// EXITS 2 PRINTING NOTHING. That is the last-occurrence trap above made worse:
/// a setting does not switch off silently, the daemon dies silently. The
/// property is held by `a_line_appended_to_the_rendered_configuration_is_a_key_of_its_own`
/// in `tests/golden_test.rs`, and by all four goldens, which now end with the
/// newline byte.
///
/// This type renders text and decides nothing. [`Self::ipv4_only`] is a
/// decision the enable path made by probing the host; the three
/// `tls_*_key` fields are a platform fact the caller read off
/// `DistroAdapter::vsftpd_tls_version_keys()`. Neither is chosen here, which is
/// what keeps this crate free of a distribution's name — it never learns which
/// family it is rendering for.
///
/// Every value reaching a field has been validated by the caller. That matters
/// in a line-oriented config: a newline in a path or an address would append
/// directives of somebody else's choosing to a file a root daemon reads
/// (rules/security.md §4). The values are validated, not escaped.
#[derive(Template)]
// A config file is not a document: HTML-escaping a path or an address would
// corrupt it silently. Values reaching a template are validated
// (rules/rust.md "Validation first"), which is what makes an escaper needless
// here rather than merely inconvenient.
#[template(path = "vsftpd/vsftpd.conf.j2", escape = "none")]
pub struct VsftpdDaemonConfig {
    /// Absolute path of the certificate chain the daemon serves.
    pub certificate_path: String,
    /// Absolute path of that certificate's private key.
    pub private_key_path: String,
    /// Lowest port of the passive data range, inclusive.
    ///
    /// The range is fixed rather than negotiated because the firewall has to be
    /// told about it in advance: the kernel's FTP conntrack helper, which would
    /// open these dynamically, reads the `PASV` reply off a control channel
    /// this design keeps encrypted.
    pub passive_port_min: u16,
    /// Highest port of the passive data range, inclusive.
    pub passive_port_max: u16,
    /// The address to advertise in the `PASV` reply, for a host behind NAT
    /// whose public address is not the one the socket is bound to.
    ///
    /// `None` on the ordinary host, where the key is not written at all and
    /// vsftpd answers with the address the control connection arrived on.
    pub passive_address: Option<String>,
    /// The daemon's concurrent-session ceiling.
    ///
    /// A product decision the panel owns; this crate receives the number.
    pub max_clients: u32,
    /// Absolute path of the transfer log.
    pub log_path: String,
    /// Whether to render the IPv4-only listen pair instead of the dual-stack
    /// one.
    ///
    /// The two pairs are mutually exclusive — setting both `listen` and
    /// `listen_ipv6` is a refusal — so this selects between them and never adds
    /// a line. Decided by the enable path, which probed an IPv6 bind the way
    /// vsftpd would and was refused by the kernel; reported in the status,
    /// never offered as a setting.
    pub ipv4_only: bool,
    /// The option NAME that switches TLS 1.0, written verbatim.
    ///
    /// `ssl_tlsv1` on both families today, and carried as a value all the same:
    /// it is one of a family of four options whose siblings ARE spelled
    /// differently, and the one spelled alike is the one that tempts a
    /// contributor to type it. See [`Self::tls_v1_2_key`] for what the wrong
    /// spelling costs.
    pub tls_v1_key: &'static str,
    /// The option NAME that switches TLS 1.1, written verbatim.
    ///
    /// `ssl_tlsv11` on the Debian family, `ssl_tlsv1_1` on the RHEL family.
    pub tls_v1_1_key: &'static str,
    /// The option NAME that switches TLS 1.2, written verbatim.
    ///
    /// `ssl_tlsv12` on the Debian family, `ssl_tlsv1_2` on the RHEL family.
    /// The wrong spelling is an unrecognised variable, and the two families
    /// report that very differently: the RHEL family prints `500 OOPS:
    /// unrecognised variable in config file: <key>`, while the Debian family's
    /// build **exits 2 printing nothing at all** — a daemon that never starts
    /// and never says why. That asymmetry is why the name is a value and not a
    /// literal.
    pub tls_v1_2_key: &'static str,
}

impl VsftpdDaemonConfig {
    /// Renders the complete daemon configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
