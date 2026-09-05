//! Entry point: tracing setup, flag parsing, server start.
#![forbid(unsafe_code)]

use maran_agent::config::invocation::{Invocation, USAGE};
use maran_agent::peercred::PeerPolicy;
use maran_agent::server;
use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_ops::firewall::{FirewallRule, NftablesProtocol, RulesetPorts, RulesetState};
use maran_templates::nftables::nftables_bans_table::NftablesBansTable;

/// Environment variable controlling the tracing filter.
const LOG_FILTER_VARIABLE: &str = "MARAN_AGENT_LOG";

/// Filter used when the environment says nothing.
const DEFAULT_LOG_FILTER: &str = "info";

/// Exit code used for every fatal startup failure.
const FAILURE_EXIT_CODE: i32 = 1;

/// The plaintext web port the seed ruleset opens.
///
/// Seeded open because a hosting server that answers no HTTP is not a hosting
/// server, and because the alternative — installing behind a drop policy and
/// opening it afterwards — means every install ends with a site nobody can
/// reach until a second step nobody documented. An administrator who wants it
/// shut denies it through the panel, which removes it like any other rule.
const HTTP_PORT: u32 = 80;

/// The TLS web port the seed ruleset opens. See [`HTTP_PORT`].
const HTTPS_PORT: u32 = 443;

#[tokio::main]
async fn main() {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_env(LOG_FILTER_VARIABLE)
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(DEFAULT_LOG_FILTER)),
        )
        .init();

    // Reading the uid can only fail if /proc is absent, which on Linux means the
    // host is not one the agent can manage.
    let default_uid = match current_uid() {
        Ok(uid) => uid,
        Err(error) => {
            tracing::error!(%error, "cannot read this process's uid");
            std::process::exit(FAILURE_EXIT_CODE);
        }
    };

    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let options = match Invocation::parse(&arguments, default_uid) {
        Ok(Invocation::Run(options)) => options,
        // Printed rather than logged: usage is the answer the person asked for,
        // and the tracing filter must not be able to swallow it.
        Ok(Invocation::ShowUsage) => {
            print!("{USAGE}");
            return;
        }
        // The two render subcommands, printed for the same reason and through
        // the same channel: the installer redirects standard output into the
        // file it seeds, so a line the tracing filter could suppress would
        // produce a truncated ruleset rather than a missing message. This is
        // the ONLY unit in the agent that prints (rules/rust.md "Logging").
        Ok(Invocation::RenderFirewallRuleset(ports)) => {
            match seed_ruleset(&ports) {
                Ok(rendered) => print!("{rendered}"),
                Err(error) => {
                    tracing::error!(%error, "the firewall ruleset could not be rendered");
                    std::process::exit(FAILURE_EXIT_CODE);
                }
            }
            return;
        }
        Ok(Invocation::RenderFirewallBans) => {
            match (NftablesBansTable {}).render_config() {
                Ok(rendered) => print!("{rendered}"),
                Err(error) => {
                    tracing::error!(%error, "the firewall bans table could not be rendered");
                    std::process::exit(FAILURE_EXIT_CODE);
                }
            }
            return;
        }
        Err(error) => {
            tracing::error!(%error, "invalid command line");
            std::process::exit(FAILURE_EXIT_CODE);
        }
    };

    if let Err(error) =
        server::serve(options.socket_path(), PeerPolicy::new(options.allow_uid)).await
    {
        tracing::error!(%error, "agent failed");
        std::process::exit(FAILURE_EXIT_CODE);
    }
}

/// Renders the ruleset a freshly installed host starts with.
///
/// # Why this goes through `ops::firewall`'s rule store rather than the render
/// type directly
///
/// The file printed here is not scratch output: the installer writes it to
/// `AgentPaths::nftables_ruleset_path()`, and from that moment it IS the
/// agent's rule store. The first `AllowPort` after an install reads it back
/// with `RulesetState::parse`, which refuses anything this agent's own
/// templates would not have produced — so a seed built by filling the template
/// struct by hand risks a `ForeignRuleset` on the first mutation, and risks
/// disagreeing about which rules the host is running.
///
/// Building it as `RulesetState` closes both: parse and render are inverses
/// there, the two seeded allows are read back as the two rules they are (so the
/// panel can deny them like any other), and the SSH routing that decides
/// whether the unconditional fallback is rendered happens in the one place that
/// implements it — rather than being reproduced here and diverging the day
/// somebody installs on a host whose sshd listens on 80.
///
/// # Errors
///
/// Returns the failure of validating the two web ports or of rendering the
/// template. Neither can happen with the constants above and a template that
/// matches its render type; both are reported rather than assumed away, because
/// a root binary that prints a half-built firewall to the installer's redirect
/// is worse than one that exits non-zero.
fn seed_ruleset(ports: &RulesetPorts) -> Result<String, Box<dyn std::error::Error>> {
    let mut state = RulesetState::empty();

    for port in [HTTP_PORT, HTTPS_PORT] {
        state = state.with(&FirewallRule {
            port: Port::parse(port)?,
            protocol: NftablesProtocol::Tcp,
            source: SourceCidr::any_v4(),
        });
    }

    Ok(state.render(ports)?)
}
