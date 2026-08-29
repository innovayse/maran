#!/usr/bin/env bash
# Puts Maran's user-local developer toolchains (dotnet 9, rustup/cargo, protoc) on
# PATH ahead of any system-wide versions (e.g. an older distro dotnet). Must be sourced,
# not executed, so the exports reach the calling shell: `source scripts/dev-env.sh`.
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$HOME/.local/bin:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

# Development-only encryption key. The panel refuses to boot without one (rules/security.md), and
# production reads it from /etc/maran/panel.env, written by the installer. This value is a
# throwaway for local runs and MUST NOT appear on any server.
export Security__EncryptionKey="MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="

# GitHub API token for reading CI runs and pushing, kept OUTSIDE the repository so it can never
# be committed: ~/.config/maran/github-token, mode 0600. Create it with:
#   mkdir -p ~/.config/maran && printf '%s\n' "<token>" > ~/.config/maran/github-token
#   chmod 600 ~/.config/maran/github-token
if [ -r "$HOME/.config/maran/github-token" ]; then
  GH_TOKEN="$(cat "$HOME/.config/maran/github-token")"
  export GH_TOKEN
  export GITHUB_TOKEN="$GH_TOKEN"
fi
