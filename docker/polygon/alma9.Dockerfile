# Development-only minimal container for testing the Rust agent on AlmaLinux 9.
# Production never uses Docker — see spec §2.
# This image is minimal: no compilers, toolchains, or build tools.
# The agent binary is built on the host and mounted at runtime.

FROM almalinux:9

# Install only required runtime dependencies
RUN yum install -y --nodocs ca-certificates \
    && yum clean all

# Create the agent socket directory
RUN mkdir -p /run/maran && chmod 755 /run/maran

# Expected docker run invocation for testing:
# docker run --rm -v "$PWD/agent/target/debug/maran-agent:/usr/local/bin/maran-agent:ro" maran-polygon-alma9 maran-agent --socket /run/maran/agent.sock --allow-uid 0
