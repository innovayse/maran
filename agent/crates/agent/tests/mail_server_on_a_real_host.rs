//! Mail enabled through the agent's own operations, against the real daemons, and LEFT RUNNING.
//!
//! This suite is the first half of the mail lane (docker/polygon/asserts/mail-lane.sh). The lane's two
//! shell assertions (local submission binding, port 25 lockdown) judge what the AGENT rendered: Postfix's
//! `main.cf` and `master.cf` with `cleanup_local`, `self_clients.cidr`, Rspamd's local-submission rule and
//! maps, and the nftables table `inet maran`. Nothing else renders those, so this suite renders them the
//! only honest way: `enable_mail`, `allow_port` and `set_local_submission`, against a real Postfix,
//! Dovecot, Rspamd and key-value server.
//!
//! What is settled here and nowhere else:
//!
//! - `enable_mail` succeeds end to end, including its wait for the kernel to show the mail ports bound
//!   publicly, and repeating it converges.
//! - The rendering carries the pieces the lane's assertions stand on.
//! - A binding made through `set_local_submission` reaches Postfix's own table (asked of `postmap`) and
//!   an account that was never bound is not in it; removing the binding takes it out again.
//!
//! It deliberately does NOT disable mail at the end: the daemons, the rendered files and the table are
//! the state the lane's assertions run against. It needs `docker run --privileged` (nftables, a real
//! mail queue) and the polygon `systemctl`, which drives the mail daemons for real once
//! `PolygonMail::switch_on_real_daemons` has been called.
//!
//! **UNOBSERVED HERE: the service manager**, stated in full in `fixtures/polygon_mail.rs`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// Shared with many suites; this one uses only part of it.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// Shared with the FTPS and panel certificate suites; this one issues into the store only.
#[allow(dead_code)]
#[path = "fixtures/polygon_certificate_authority.rs"]
mod polygon_certificate_authority;
#[path = "fixtures/polygon_mail.rs"]
mod polygon_mail;

use std::path::Path;
use std::process::Command;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::mail::local_submission::LocalSubmission;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_ops::firewall::{
    FirewallRule, NftablesProtocol, PortSpan, ProcessFirewallHost, RulesetPorts, allow_port,
};
use maran_ops::mail::{
    PortBinding, ProcessMailHost, enable_mail, remove_local_submission, set_local_submission,
};

use polygon_account::PolygonAccount;
use polygon_certificate_authority::PolygonCertificateAuthority;
use polygon_mail::PolygonMail;

/// The SSH port the firewall ruleset is rendered for.
const SSH_PORT: u32 = 22;

/// The panel port the firewall ruleset is rendered for.
const PANEL_PORT: u32 = 8443;

/// The accounts bound in this suite.
const BOUND_ACCOUNT: &str = "mailbound";
const UNBOUND_ACCOUNT: &str = "mailunbound";

/// The domain the bound account may send from, and its hourly limit.
const BOUND_DOMAIN: &str = "bound.test";
const SEND_LIMIT: u32 = 5;

/// Asks Postfix, through `postmap`, what `key` maps to in the local-submit users table.
///
/// `None` when the key is absent (`postmap -q` exits 1 for no match).
fn submit_users_lookup(key: &str) -> Option<String> {
    let distro = PolygonMail::distro();
    let table = AgentPaths::mail_local_submit_users_path(distro.postfix_config_directory());
    let spec = format!("{}:{}", distro.postfix_map_type(), table.display());
    let output = Command::new(distro.postmap_binary().as_path())
        .args(["-q", key, &spec])
        .output()
        .expect("postmap must be runnable");
    output
        .status
        .success()
        .then(|| String::from_utf8_lossy(&output.stdout).trim().to_owned())
}

/// The firewall rule that opens SMTP to everyone, the way a mail server's operator does.
fn smtp_rule() -> FirewallRule {
    FirewallRule {
        ports: PortSpan::single(Port::parse(25).expect("25 is a port")),
        protocol: NftablesProtocol::Tcp,
        source: SourceCidr::parse("0.0.0.0/0").expect("a valid source network"),
    }
}

#[test]
#[ignore = "installs real mail configuration and starts real Postfix, Dovecot, Rspamd and a key-value server: polygon only"]
fn mail_is_enabled_through_the_agent_and_the_stack_is_left_running() {
    PolygonMail::require_polygon();
    let distro = PolygonMail::distro();
    let version_id = PolygonMail::version_id();
    let settings = PolygonMail::settings();
    let host = ProcessMailHost::new();

    let authority = PolygonCertificateAuthority::create(distro, "polygon-mail");
    PolygonMail::install_certificate(&authority);
    PolygonMail::switch_on_real_daemons();

    // Enabling: every daemon installed and active, and the kernel shows the five mail ports bound to
    // an address other hosts can reach (the operation waits for exactly that; the status is read again).
    let state = enable_mail(&host, distro, &version_id, &settings)
        .unwrap_or_else(|error| panic!("enabling mail must succeed in the polygon: {error}"));
    assert!(
        state
            .daemons
            .iter()
            .all(|daemon| daemon.installed && daemon.active),
        "every mail daemon must be installed and active: {:?}",
        state.daemons
    );
    for port in [25_u16, 465, 587, 143, 993] {
        let bound = state.ports.iter().find(|state| state.port == port);
        assert_eq!(
            bound.map(|state| state.binding),
            Some(PortBinding::Public),
            "port {port} must be bound publicly"
        );
    }

    // The rendering the lane's assertions judge, read from disk where the daemons read it.
    let postfix_directory = Path::new(distro.postfix_config_directory());
    let master = PolygonMail::read_rendered(&postfix_directory.join("master.cf"));
    assert!(
        master.contains("cleanup_local"),
        "master.cf must carry the cleanup_local service of the local-submission road"
    );
    let self_clients = PolygonMail::read_rendered(Path::new(AgentPaths::MAIL_SELF_CLIENTS_PATH));
    assert!(
        self_clients
            .lines()
            .any(|line| !line.trim_start().starts_with('#') && !line.trim().is_empty()),
        "self_clients.cidr must name at least this host's own addresses: {self_clients:?}"
    );
    let rspamd_directory = Path::new(distro.rspamd_config_directory());
    for rendered in [
        "local.d/maran_local_submit.conf",
        "plugins.d/maran_local_submit.lua",
        "rspamd.conf.local",
    ] {
        assert!(
            rspamd_directory.join(rendered).is_file(),
            "{rendered} must be rendered into {rspamd_directory:?}"
        );
    }

    // Converging: the same call again changes nothing it should not, and still reports a running stack.
    let again = enable_mail(&host, distro, &version_id, &settings)
        .unwrap_or_else(|error| panic!("enabling mail again must converge: {error}"));
    assert!(again.daemons.iter().all(|daemon| daemon.active));

    // The second layer of the port 25 lockdown is the agent's firewall table; an operator opens SMTP in it.
    let ports = RulesetPorts {
        ssh_ports: vec![Port::parse(SSH_PORT).expect("a valid port")],
        panel_port: Port::parse(PANEL_PORT).expect("a valid port"),
    };
    allow_port(&ProcessFirewallHost::new(), distro, &ports, &smtp_rule())
        .unwrap_or_else(|error| panic!("allowing SMTP must succeed in the polygon: {error}"));
    let table = Command::new("nft")
        .args(["list", "chain", "inet", "maran", "output"])
        .output()
        .expect("nft must be runnable");
    assert!(
        String::from_utf8_lossy(&table.stdout).contains("dport 25"),
        "the output chain of table inet maran must restrict port 25: {}",
        String::from_utf8_lossy(&table.stderr)
    );

    // The binding: an account bound through the agent reaches Postfix's own table, one never bound does not.
    let bound = PolygonAccount::create(BOUND_ACCOUNT);
    let unbound = PolygonAccount::create(UNBOUND_ACCOUNT);
    assert_eq!(
        submit_users_lookup(BOUND_ACCOUNT),
        None,
        "control: an account is not admitted before it is bound"
    );
    let binding = LocalSubmission::parse(
        BOUND_ACCOUNT,
        bound.ids().uid(),
        &[BOUND_DOMAIN],
        SEND_LIMIT,
        &[] as &[&str],
        &[] as &[(&str, u32)],
    )
    .expect("the binding must be valid");
    set_local_submission(&host, distro, &binding)
        .unwrap_or_else(|error| panic!("binding an account must succeed in the polygon: {error}"));
    assert_eq!(
        submit_users_lookup(BOUND_ACCOUNT).as_deref(),
        Some("OK"),
        "the bound account must be admitted by Postfix's table"
    );
    assert_eq!(
        submit_users_lookup(unbound.name().as_str()),
        None,
        "an account that was never bound must not be admitted"
    );
    let accounts =
        PolygonMail::read_rendered(Path::new(AgentPaths::MAIL_LOCAL_SUBMIT_ACCOUNTS_PATH));
    assert!(
        accounts.contains(&format!("{} {BOUND_ACCOUNT}", bound.ids().uid())),
        "the uid-to-account map must carry the binding: {accounts:?}"
    );

    // Removing the binding takes it out of both tables. The stack stays up for the lane.
    remove_local_submission(&host, distro, BOUND_ACCOUNT)
        .unwrap_or_else(|error| panic!("unbinding must succeed in the polygon: {error}"));
    assert_eq!(
        submit_users_lookup(BOUND_ACCOUNT),
        None,
        "a removed binding must no longer be admitted"
    );
}
