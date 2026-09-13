//! What the command line asked this process to do.

use maran_agent_core::validation::web::port::Port;
use maran_ops::firewall::RulesetPorts;

use super::agent_options::{ALLOW_UID_FLAG, AgentOptions, DEFAULT_SOCKET, SOCKET_FLAG};
use super::options_error::OptionsError;

/// Flags asking for the usage text instead of a running daemon.
const HELP_FLAGS: [&str; 2] = ["--help", "-h"];

/// Subcommand printing the seed nftables ruleset the installer writes.
const RENDER_FIREWALL_RULESET: &str = "render-firewall-ruleset";

/// Subcommand printing the nftables bans table the installer writes.
const RENDER_FIREWALL_BANS: &str = "render-firewall-bans";

/// Flag naming a port the host's sshd listens on. Repeatable.
const SSH_PORT_FLAG: &str = "--ssh-port";

/// Flag naming the public port the panel is reachable on.
const PANEL_PORT_FLAG: &str = "--panel-port";

/// The usage text, printed for [`Invocation::ShowUsage`].
///
/// Written out rather than derived from an argument-parsing library: the agent
/// takes two flags and two subcommands, and a dependency that renders help for
/// those would be a dependency in a root daemon, which is the one place where
/// the cost of a dependency is not measured in build time.
pub const USAGE: &str = concat!(
    "maran-agent — the Maran root daemon\n",
    "\n",
    "Usage: maran-agent [--socket <path>] [--allow-uid <uid>]\n",
    "       maran-agent render-firewall-ruleset --ssh-port <port> [--ssh-port <port>]…\n",
    "                                          --panel-port <port>\n",
    "       maran-agent render-firewall-bans\n",
    "\n",
    "  --socket <path>     Unix socket to bind (default: /run/maran/agent.sock)\n",
    "  --allow-uid <uid>   The single uid permitted to use the agent\n",
    "                      (default: the uid this process runs as)\n",
    "  -h, --help          Print this text and exit\n",
    "\n",
    "Render subcommands print one file to standard output and exit. They start\n",
    "no daemon and write nothing: the installer redirects them into the files it\n",
    "seeds, so the seed comes from the same templates the agent later applies.\n",
    "\n",
    "  render-firewall-ruleset   The starting policy: loopback, connection state,\n",
    "                            ICMP, the two ports below, and the web ports.\n",
    "    --ssh-port <port>       A port this host's sshd listens on. Required, and\n",
    "                            REPEATABLE — sshd listens on every Port directive\n",
    "                            and every ListenAddress host:port, so pass them\n",
    "                            all. Never defaulted: a wrong guess renders a drop\n",
    "                            policy that locks the operator out.\n",
    "    --panel-port <port>     The public port the panel is reachable on.\n",
    "                            Required, for the same reason.\n",
    "  render-firewall-bans      The table runtime bans are added to.\n",
);

/// What the agent was asked to do.
///
/// An enum rather than a flag on [`AgentOptions`], because the arms do
/// different things: one binds a socket and serves as root, the others write to
/// standard output and exit. A boolean would let a caller do both, or neither.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Invocation {
    /// Run the daemon with these options.
    Run(AgentOptions),
    /// Print [`USAGE`] and exit successfully.
    ShowUsage,
    /// Print the seed nftables ruleset for these two host ports, and exit.
    ///
    /// It carries [`RulesetPorts`] rather than two `Port` values so that the
    /// two are never side by side as bare arguments anywhere: they are placed
    /// into named fields the moment each flag is parsed, and read out by name
    /// where the render happens. Two values of one type in a row can be
    /// swapped without the compiler saying a word, and this particular swap
    /// renders SSH's hard allow for the panel's port and the panel's for SSH's.
    RenderFirewallRuleset(RulesetPorts),
    /// Print the nftables bans table, and exit. It takes no parameters — the
    /// table's text is constant, and every ban is an element added at runtime.
    RenderFirewallBans,
}

impl Invocation {
    /// Parses `arguments`, which must not include the program name.
    ///
    /// `default_uid` is used when `--allow-uid` is absent — the installer always
    /// passes the panel user's uid explicitly, while a developer running the
    /// agent by hand gets their own uid and a working socket.
    ///
    /// **An unrecognised argument is refused.** It used to be ignored, on the
    /// reasoning that a unit file gaining a flag an older binary does not know
    /// must not stop the agent from coming up. That trade was the wrong way
    /// round, and it was observed: `maran-agent --help` printed nothing, was
    /// parsed as "no flags at all", and started a REAL root daemon on the
    /// default socket path with the default uid — taking the socket from the
    /// agent already serving it and answering with an access rule nobody chose.
    /// A unit file naming a flag the binary does not have is a packaging error,
    /// and a service that fails loudly on one is strictly better than a root
    /// daemon running a configuration its operator did not write.
    ///
    /// A malformed value of a *known* flag is refused for the same reason.
    /// `--allow-uid` decides who may drive a root daemon, and silently falling
    /// back to a default because the value did not parse is how a typo in a unit
    /// file turns into an access rule nobody chose.
    ///
    /// # The order of the three decisions, which is fixed on purpose
    ///
    /// 1. `--help` anywhere wins, swept across the WHOLE command line before
    ///    anything else, so it still answers when it stands beside the very flag
    ///    the person is asking about — including beside a subcommand.
    /// 2. A subcommand is matched at `arguments[0]` and nowhere else. First
    ///    position only, so `--socket render-firewall-bans` binds a socket with
    ///    an odd name rather than printing a file, and so a subcommand's own
    ///    flags cannot leak into the daemon's parse.
    /// 3. Everything else is the daemon's flag loop, unchanged. It has never
    ///    heard of `--ssh-port` or `--panel-port` and refuses them as unknown,
    ///    which is what stops `maran-agent --ssh-port 22` from starting a root
    ///    daemon that ignored an argument its operator meant.
    ///
    /// The ordering matters because this file's history is the reason it is
    /// written down: sloppy argv handling here once started a stray root daemon.
    ///
    /// # Errors
    ///
    /// Returns [`OptionsError::UnknownFlag`] for an argument this binary does
    /// not define in the position it appeared, [`OptionsError::MissingValue`]
    /// for a flag left dangling or a required flag never given,
    /// [`OptionsError::InvalidUid`] when `--allow-uid` is not a number,
    /// [`OptionsError::InvalidPort`] when a render subcommand's port flag is
    /// not a number between 1 and 65535, and [`OptionsError::RepeatedFlag`]
    /// when `--panel-port` is given twice.
    pub fn parse(arguments: &[String], default_uid: u32) -> Result<Self, OptionsError> {
        // Swept across the WHOLE command line before anything is validated, so
        // `--help` still answers when it stands beside the very flag the person
        // is asking about. Checking it in the loop below would refuse the one
        // invocation that explains the mistake.
        if arguments
            .iter()
            .any(|argument| HELP_FLAGS.contains(&argument.as_str()))
        {
            return Ok(Self::ShowUsage);
        }

        // The subcommand, at the first position and only there. Before the flag
        // loop, so the loop never sees a subcommand's own flags; and matched by
        // position, so a value that happens to spell a subcommand's name cannot
        // change what this process does.
        match arguments.first().map(String::as_str) {
            Some(RENDER_FIREWALL_RULESET) => {
                return Self::parse_render_ruleset(&arguments[1..]);
            }
            Some(RENDER_FIREWALL_BANS) => {
                return Self::parse_render_bans(&arguments[1..]);
            }
            _ => {}
        }

        let mut options = AgentOptions {
            socket_path: std::path::PathBuf::from(DEFAULT_SOCKET),
            allow_uid: default_uid,
        };

        let mut remaining = arguments.iter();
        while let Some(argument) = remaining.next() {
            match argument.as_str() {
                SOCKET_FLAG => {
                    let value = remaining
                        .next()
                        .ok_or(OptionsError::MissingValue { flag: SOCKET_FLAG })?;
                    options.socket_path = std::path::PathBuf::from(value);
                }
                ALLOW_UID_FLAG => {
                    let value = remaining.next().ok_or(OptionsError::MissingValue {
                        flag: ALLOW_UID_FLAG,
                    })?;
                    options.allow_uid = value.parse().map_err(|_| OptionsError::InvalidUid {
                        value: value.clone(),
                    })?;
                }
                unknown => {
                    return Err(OptionsError::UnknownFlag {
                        flag: unknown.to_owned(),
                    });
                }
            }
        }

        Ok(Self::Run(options))
    }

    /// Parses what follows `render-firewall-ruleset`.
    ///
    /// Both port flags are REQUIRED and neither has a default, which is the
    /// whole point of the subcommand existing: the file it prints is a
    /// `policy drop` ruleset, so a port it does not name is a port nothing can
    /// reach. A defaulted 22 on a host whose sshd listens elsewhere would seed
    /// a firewall that locks the installing operator out of the machine they
    /// are installing on, with no remote recovery.
    ///
    /// `--ssh-port` may be given more than once and every occurrence is kept:
    /// a host can serve SSH on several ports at once, and the seeded policy
    /// accepts the union. Given none, the subcommand is refused exactly as a
    /// dangling flag is.
    ///
    /// `--panel-port` may NOT be repeated, and a second one is refused rather
    /// than quietly replacing the first. The panel has one public port, so a
    /// repeat means the caller thinks otherwise; last-wins would seed a
    /// firewall that opens a port the panel is not on.
    ///
    /// # Errors
    ///
    /// Returns [`OptionsError::MissingValue`] for a flag left dangling or never
    /// given, [`OptionsError::InvalidPort`] for a value outside 1..=65535,
    /// [`OptionsError::RepeatedFlag`] for a second `--panel-port`, and
    /// [`OptionsError::UnknownFlag`] for anything else.
    fn parse_render_ruleset(arguments: &[String]) -> Result<Self, OptionsError> {
        let mut ssh_ports = Vec::new();
        let mut panel_port = None;

        let mut remaining = arguments.iter();
        while let Some(argument) = remaining.next() {
            let flag = match argument.as_str() {
                SSH_PORT_FLAG => SSH_PORT_FLAG,
                PANEL_PORT_FLAG => PANEL_PORT_FLAG,
                unknown => {
                    return Err(OptionsError::UnknownFlag {
                        flag: unknown.to_owned(),
                    });
                }
            };

            let value = remaining
                .next()
                .ok_or(OptionsError::MissingValue { flag })?;
            let port = parse_port(flag, value)?;

            if flag == SSH_PORT_FLAG {
                // Repeatable and accumulated, because sshd listens on every
                // `Port` directive and every `ListenAddress host:port` it is
                // given. A repeated flag that OVERWROTE would seed a firewall
                // that opens the last one and closes the others.
                ssh_ports.push(port);
            } else if panel_port.replace(port).is_some() {
                // Repeated, and refused rather than last-wins. The panel has
                // exactly one public port, so a second `--panel-port` means the
                // caller believes otherwise — and silently keeping the last one
                // would seed a firewall that opens a port the panel is not on
                // and closes the one it is, with no remote recovery. The
                // asymmetry with `--ssh-port` is the difference between the two
                // facts, not an oversight: sshd really can listen on several
                // ports, and nginx's panel vhost cannot.
                return Err(OptionsError::RepeatedFlag {
                    flag: PANEL_PORT_FLAG,
                });
            }
        }

        if ssh_ports.is_empty() {
            return Err(OptionsError::MissingValue {
                flag: SSH_PORT_FLAG,
            });
        }

        Ok(Self::RenderFirewallRuleset(RulesetPorts {
            ssh_ports,
            panel_port: panel_port.ok_or(OptionsError::MissingValue {
                flag: PANEL_PORT_FLAG,
            })?,
        }))
    }

    /// Parses what follows `render-firewall-bans`, which is nothing.
    ///
    /// # Errors
    ///
    /// Returns [`OptionsError::UnknownFlag`] for any argument at all. The table
    /// this subcommand prints is constant text, so an argument here means the
    /// caller believes it is parameterised and it is not — and silently
    /// ignoring it would print a file that does not answer what was asked.
    fn parse_render_bans(arguments: &[String]) -> Result<Self, OptionsError> {
        if let Some(unknown) = arguments.first() {
            return Err(OptionsError::UnknownFlag {
                flag: unknown.clone(),
            });
        }

        Ok(Self::RenderFirewallBans)
    }
}

/// Parses one port flag's value.
///
/// The range is not checked here: [`Port::parse`] is the one place in this
/// workspace that decides what a port is, and duplicating its bounds would
/// leave two answers to drift apart.
///
/// # Errors
///
/// Returns [`OptionsError::InvalidPort`] when the value is not a number, or is
/// a number outside 1..=65535 — which includes 0, the value a firewall reads as
/// "any port".
fn parse_port(flag: &'static str, value: &str) -> Result<Port, OptionsError> {
    value
        .parse::<u32>()
        .ok()
        .and_then(|number| Port::parse(number).ok())
        .ok_or_else(|| OptionsError::InvalidPort {
            flag,
            value: value.to_owned(),
        })
}

#[cfg(test)]
#[path = "../tests/config/invocation_tests.rs"]
mod tests;
