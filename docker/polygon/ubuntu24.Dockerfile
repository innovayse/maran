# Development-only minimal container for testing the Rust agent on Ubuntu 24.04.
# Production never uses Docker — see spec §2.
# This image is minimal: no compilers, toolchains, or build tools.
# The agent binary is built on the host and mounted at runtime.

FROM ubuntu:24.04

# Install only required runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Create the agent socket directory
RUN mkdir -p /run/maran && chmod 755 /run/maran

# Expected docker run invocation for testing:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-ubuntu24 maran-agent --socket /run/maran/agent.sock --allow-uid 0
