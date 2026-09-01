//! Entry point: tracing setup, flag parsing, server start.
#![forbid(unsafe_code)]

use maran_agent::config::agent_options::AgentOptions;
use maran_agent::peercred::PeerPolicy;
use maran_agent::server;
use maran_agent_core::utils::current_uid::current_uid;

/// Environment variable controlling the tracing filter.
const LOG_FILTER_VARIABLE: &str = "MARAN_AGENT_LOG";

/// Filter used when the environment says nothing.
const DEFAULT_LOG_FILTER: &str = "info";

/// Exit code used for every fatal startup failure.
const FAILURE_EXIT_CODE: i32 = 1;

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
    let options = match AgentOptions::parse(&arguments, default_uid) {
        Ok(options) => options,
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
