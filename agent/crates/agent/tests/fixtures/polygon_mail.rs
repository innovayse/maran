//! What the mail suite needs from the polygon host before it enables mail through the agent.
//!
//! **The configuration and the daemons are the agent's and the distribution's own.** This fixture writes
//! no Postfix, Dovecot, Rspamd or key-value file: `ops::mail::enable_mail` renders them, and the only
//! things supplied from outside are the two inputs the agent refuses to invent, a certificate for the
//! mail hostname in the panel's store (the agent never writes into that store) and the polygon service
//! manager's willingness to start the daemons.
//!
//! **UNOBSERVED HERE: the service manager.** The polygon images ship a `systemctl` stand-in that records
//! a unit's state and starts nothing, so `enable_mail`'s wait for the mail ports could never succeed.
//! [`PolygonMail::switch_on_real_daemons`] creates the marker file that makes the stand-in drive the real daemons by
//! their own programs at the moment the agent asks (docker/polygon/stand-ins/systemctl-stand-in.sh,
//! "Real mail daemons"). The daemons, their configuration and the kernel's sockets are real; the thing
//! that would launch them on a server is not.

use std::path::Path;

use maran_agent_core::validation::mail::mail_message_size_mib::MailMessageSizeMib;
use maran_agent_core::validation::mail::mail_server_hostname::MailServerHostname;
use maran_agent_core::validation::mail::spam_thresholds::SpamThresholds;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for, detect};
use maran_ops::mail::MailSettings;

use crate::polygon_certificate_authority::PolygonCertificateAuthority;

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The directory the stand-in records unit state in.
const STAND_IN_STATE_DIRECTORY: &str = "/run/polygon-units";

/// The marker the stand-in looks for, named exactly as in the stand-in script.
const REAL_DAEMONS_MARKER: &str = "/run/polygon-units/.mail-daemons-are-real";

/// The name the mail server introduces itself with, and its certificate is filed under.
pub const HOSTNAME: &str = "mail.polygon.test";

/// The largest message the polygon stack accepts, in MiB.
const MESSAGE_SIZE_MIB: u32 = 25;

/// The spam filter's three scores: greylist, add header, reject.
const SPAM_SCORES: (u32, u32, u32) = (4, 6, 15);

/// The mail suite's view of the polygon host: every fact and preparation it needs, as associated functions.
pub struct PolygonMail;

impl PolygonMail {
    /// Refuses to go on unless this process is root inside a polygon image on a family that offers mail.
    ///
    /// A panic and not a quiet `return`: these suites are `#[ignore]`d, so a skip would report as a pass and
    /// a suite that never spoke to a mail daemon would count as coverage of one (rules/testing.md).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent, the process is not root, the host is outside the support
    /// matrix, or the family is SUSE, which is not offered mail.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "this suite installs real mail configuration and starts real daemons and must run only inside a \
             polygon container: {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "enabling mail requires root"
        );
        assert!(
            !matches!(Self::distro().family(), DistroFamily::Suse),
            "mail is not offered on the SUSE family, so this suite has no stack to start there"
        );
    }

    /// The adapter of this host.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix, which a polygon image never is.
    pub fn distro() -> &'static dyn DistroAdapter {
        adapter_for(
            detect()
                .expect("a polygon image is a supported host")
                .family,
        )
    }

    /// The os-release `VERSION_ID`, which picks the Dovecot generation and the key-value server.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix.
    pub fn version_id() -> String {
        detect()
            .expect("a polygon image is a supported host")
            .version_id
    }

    /// Makes the polygon `systemctl` start, reload and stop the mail daemons for real.
    ///
    /// The marker is left in place on purpose: the mail lane runs its assertions after this suite has exited,
    /// and they reload Rspamd through the same `systemctl`.
    ///
    /// # Panics
    ///
    /// Panics when the marker cannot be written.
    pub fn switch_on_real_daemons() {
        std::fs::create_dir_all(STAND_IN_STATE_DIRECTORY).unwrap_or_else(|error| {
            panic!("{STAND_IN_STATE_DIRECTORY} must be creatable: {error}")
        });
        std::fs::write(REAL_DAEMONS_MARKER, "real\n")
            .unwrap_or_else(|error| panic!("{REAL_DAEMONS_MARKER} must be writable: {error}"));
    }

    /// Files certificate material for [`HOSTNAME`] in the panel's store, as a certificate issuance would.
    ///
    /// # Panics
    ///
    /// Panics when the hostname does not parse or the store cannot be written.
    pub fn install_certificate(authority: &PolygonCertificateAuthority) {
        let hostname = Domain::parse(HOSTNAME).expect("the polygon mail hostname must parse");
        authority.issue_into_the_panel_store(Self::distro(), &hostname);
    }

    /// The settings the suite enables mail with.
    ///
    /// # Panics
    ///
    /// Panics when a constant of this fixture is refused by its validator.
    pub fn settings() -> MailSettings {
        MailSettings {
            hostname: MailServerHostname::parse(HOSTNAME)
                .expect("the polygon mail hostname must parse"),
            pop3_enabled: false,
            message_size: MailMessageSizeMib::parse(MESSAGE_SIZE_MIB)
                .expect("the polygon message size must be accepted"),
            spam: SpamThresholds::parse(SPAM_SCORES.0, SPAM_SCORES.1, SPAM_SCORES.2)
                .expect("the polygon spam scores must be ascending"),
        }
    }

    /// Reads a rendered file the suite asserts on.
    ///
    /// # Panics
    ///
    /// Panics when the file cannot be read: the agent was to have written it.
    pub fn read_rendered(path: &Path) -> String {
        std::fs::read_to_string(path)
            .unwrap_or_else(|error| panic!("the agent was to render {path:?}: {error}"))
    }
}
