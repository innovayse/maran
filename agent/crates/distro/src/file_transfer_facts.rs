//! SFTP (OpenSSH) and FTPS (vsftpd): groups, binaries, TLS option spellings and sshd facts.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;
use crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys;

/// SFTP (OpenSSH) and FTPS (vsftpd): groups, binaries, TLS option spellings and sshd facts.
pub trait FileTransferFacts: Send + Sync {
    /// Name of the group whose members sshd's `Match Group` block chroots.
    ///
    /// SFTP is served by the OpenSSH daemon already running on the host, so an
    /// SFTP user is a system account in this group and nothing else — no FTP
    /// daemon is installed and no FTP binary is named anywhere in this trait.
    ///
    /// The name is the same on both families ON PURPOSE, not by coincidence:
    /// the installer writes ONE `Match Group <group>` block into sshd's
    /// configuration, so a family answering a different name would have that
    /// block match nobody there, and an SFTP user created on it would get a
    /// full session instead of a jail — the opposite of the isolation the block
    /// exists for. The sameness is therefore asserted by a test rather than left
    /// to the two literals happening to agree.
    fn sftp_group(&self) -> &'static str;

    /// Absolute path of the vsftpd binary, for the process-execution
    /// allow-list.
    ///
    /// This is an `argv[0]` the agent is permitted to spawn, and that is the
    /// whole of what this method decides. The daemon itself is started and
    /// stopped by the service manager, never by the agent; what the agent runs
    /// this program for is to ask it about a candidate configuration file
    /// before that file is swapped into place, which is the same
    /// render-validate-swap discipline every other config in this workspace
    /// goes through.
    ///
    /// Both families answer `/usr/sbin/vsftpd`, and the agreement is between
    /// two packages rather than a rule: it is the path in each `vsftpd`
    /// package's own file list — `dpkg -L` on the Debian family, `rpm -ql` on
    /// the RHEL family — which is the documented interface the merged-`/usr`
    /// rule on [`DistroAdapter`](crate::adapter::DistroAdapter) says to answer. It is asked of the adapter all
    /// the same, because a binary is spawnable only because this trait named
    /// it, and `ops` names no absolute path of its own.
    fn vsftpd_binary(&self) -> AdapterBinary;

    /// Name of the group whose membership is the entire authorization to use
    /// FTPS.
    ///
    /// The panel's own group rather than a distribution one, so membership
    /// means exactly "this login may transfer files over FTPS" and nothing a
    /// package created can drift into it. It is a group and not a file of
    /// names because the check that reads it is a PAM line, and PAM asks about
    /// group membership.
    ///
    /// The name is the same on both families ON PURPOSE, for the reason
    /// [`Self::sftp_group`] is: the authorization is one `pam_succeed_if.so …
    /// user ingroup <group>` line in one PAM service file, so a family
    /// answering a different name would leave that line admitting nobody
    /// there. Each family's own adapter test pins the literal, so a drift on
    /// either side is a named failure rather than two values quietly ceasing
    /// to agree.
    fn ftps_group(&self) -> &'static str;

    /// The names of the three TLS protocol-version options a rendered
    /// `vsftpd.conf` writes.
    ///
    /// What the value is FOR: the agent renders one `vsftpd.conf` per host, and
    /// three of its lines are `<key>=YES`/`<key>=NO` where `<key>` is a word
    /// this method supplies verbatim. `maran-templates` receives the three
    /// words as plain strings and never learns which family it is rendering
    /// for, which is what keeps the platform fact inside this crate.
    ///
    /// It is a method and not a constant because the two families' builds of
    /// the same vsftpd version spell the family of options differently:
    /// `ssl_tlsv11`/`ssl_tlsv12` on the Debian family, `ssl_tlsv1_1`/
    /// `ssl_tlsv1_2` on the RHEL family. Each family's spelling is pinned by a
    /// literal in that family's own adapter test, so a copy-paste of one
    /// implementation into the other is a named failure rather than a config
    /// that renders and does not start.
    ///
    /// # What the wrong spelling costs
    ///
    /// The two failures are not equally readable, and the worse one is the
    /// reason this is an adapter method rather than a comment. On the RHEL
    /// family an unknown option is reported: `500 OOPS: unrecognised variable
    /// in config file: <key>`. On the Debian family **vsftpd exits 2 and prints
    /// nothing at all** — a daemon that never starts and never says why, which
    /// gives an operator nothing to read. Both measured 2026-09-09; see
    /// [`crate::vsftpd_tls_version_keys::VsftpdTlsVersionKeys`] for the images
    /// and package versions the words themselves came off.
    fn vsftpd_tls_version_keys(&self) -> VsftpdTlsVersionKeys;

    /// Absolute path of `ssh-keygen`, for the process-execution allow-list.
    ///
    /// Used only as the VALIDATOR of an SFTP login's key file (`ops::sftp`'s
    /// `set_sftp_authorized_keys`): `ssh-keygen -l -f <file>` is OpenSSH's own
    /// parser reading the file sshd will read, and it writes nothing.
    fn ssh_keygen_binary(&self) -> AdapterBinary;

    /// Name of the OpenSSH server's service unit.
    ///
    /// `ssh` on the Debian family, `sshd` on the RHEL family. Reported, never
    /// restarted: the agent has no operation that touches the daemon its own
    /// caller may be connected through.
    ///
    /// Reported, but NOT by asking whether this unit is active — see the
    /// socket-activation warning on [`ServiceManagerFacts::managed_units`](crate::service_manager_facts::ServiceManagerFacts::managed_units) before writing any
    /// code that does. On the Debian family this unit is normally inactive on a
    /// completely healthy host.
    fn ssh_service(&self) -> &'static str;

    /// Absolute path of the OpenSSH server's main configuration file.
    ///
    /// `/etc/ssh/sshd_config` on both supported families — the OpenSSH package
    /// installs it at the identical path everywhere, so this method answers the
    /// same literal on every arm. It is still asked of the adapter rather than
    /// written as a literal in `ops`, for the same reason [`ServiceManagerFacts::service_manager`](crate::service_manager_facts::ServiceManagerFacts::service_manager)
    /// is: a value this crate names once is a value that cannot drift out of
    /// step between two call sites, and the day a family's packaging DOES
    /// diverge, one arm changes and nothing outside this crate has to know.
    ///
    /// This is the file the installer's `installer/lib/86-sftp.sh` appends its
    /// `Match Group` block to — at the end of the MAIN file, never into a
    /// drop-in — so a reader that wants to find that block reads exactly this
    /// path and no `Include` target.
    fn sshd_config_path(&self) -> &'static str;
}
