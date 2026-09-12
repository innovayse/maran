/**
 * The one way this SPA writes what a firewall rule opens: a single port, or a range.
 *
 * A rule names either one port or a run of them, and the two must never look alike on screen. A
 * range shown as its first port is ninety-nine ports an administrator does not know are open, and
 * the removal they then ask for names a rule that does not exist. Three places show it — the rules
 * table, the lockout confirmation and the presets — so the spelling lives here rather than three
 * times.
 *
 * The separator is a plain hyphen, which is how the agent's rendered nftables line writes it and
 * how the panel's audit journal records it: one rule, one spelling, wherever it is read.
 *
 * @param port The port the rule names, or the lower bound of a range.
 * @param portTo The inclusive upper bound, or `null` when the rule names a single port.
 * @returns `"8080"` for a single port, `"30000-30099"` for a range.
 */
export const portRangeLabel = (port: number, portTo: number | null): string => {
  return portTo === null ? String(port) : `${port}-${portTo}`
}
