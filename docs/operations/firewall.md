# The firewall page and its lock-out protection

What the panel refuses when an administrator edits the host firewall, why, and how to get past the
refusal (issue #159, partial: the lock-out guard). The reasoning and its limits are in
`docs/superpowers/notes/2026-11-07-firewall-lockout-guard-threat-note.md`.

## What the panel refuses

`POST /api/v1/firewall/rules` (the "Open the port" form) answers **409** with the code
`FirewallRuleLocksOutCaller` and installs nothing when all of these hold:

- the rule is **TCP** and names **one port** (not a range);
- that port is one of the host's **SSH ports** (the installer records them as `Firewall__SshPorts`);
- the host has **no rule of its own for that port yet**;
- the rule's source is **not** `0.0.0.0/0` and **does not contain the address the request came from**.

There is no override. The refusal is journalled as a failed "firewall rule allowed" entry and the
agent is not asked to change anything.

If the panel cannot read the host's current rules, or cannot tell which address you are connected
from, it does not guess: an unreadable rule list stops the change with the agent's own error, and an
unreadable caller address is treated as outside every restricted range.

## Removing a rule, and banning an address

Removing a rule is judged on the rules that would be left. `DELETE /api/v1/firewall/rules` answers
**409** `FirewallRuleLocksOutCaller` when the rule is a single-port TCP rule for an SSH port, other
rules for that port would remain, and none of them contains the address the request came from. (The
agent's unconditional SSH accept returns only when the last rule for the port is removed, so removing
the rule that admits you while another remains cuts you off.) Removing the last rule is allowed.

`POST /api/v1/firewall/bans` answers **409** `BanAddressIsCaller` for the address the request came
from, and **409** `BanAddressWhitelisted` for an address inside a whitelisted range (remove the range
from the whitelist first). A ban is applied before every other rule, so it would cut you off from SSH
and the panel at once.

## What it does not refuse, and why

- **The panel's own port.** The agent always accepts it, and no rule can close it, so there is
  nothing to guard.
- **A range, a UDP rule, or a rule for a port that is not an SSH port.** Only a single-port TCP rule
  for an SSH port replaces the unconditional accept; the others leave it where it was.
- **A rule next to an existing rule for the same SSH port.** It only widens who is admitted.
- **`0.0.0.0/0`.** It admits every source, IPv6 included. `::/0` is not the same thing: it restricts
  to IPv6, so an IPv4 operator is outside it.

## How to get past a refusal

Open the SSH port to a range that **includes the address you are connected from** (or to
`0.0.0.0/0`), then narrow it later from inside that range. Or make the change from an address that
is inside the range you want.

## What the check compares

The address of the **browser session** that sent the change, as the panel sees it (behind nginx,
the forwarded address). That is the SSH client's address only when you use one machine for both. If
you reach SSH from a different network, from a VPN split or through a jump host, the panel cannot
know: it will refuse a range that excludes your browser's address and permit one that includes it,
whatever your SSH client's address is. On a host serving SSH on several ports, narrowing any one of
them to a range without your browser's address is refused, because the panel cannot know which port
your session uses.

Two administrators changing the same SSH port within one request of each other can still defeat the
check; the listing and the change are two calls to the agent, not one.
