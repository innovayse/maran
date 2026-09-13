/**
 * The IPv4 range that admits every source — what the panel's own `AllowPortRequest` documents as
 * "allows any source", and the value the port presets send.
 *
 * A protocol constant, not domain data the SPA invented: it is the same three characters in every
 * firewall on earth, and the alternative is a port form that refuses to submit until an operator
 * types it from memory.
 */
export const ANY_IPV4_SOURCE = '0.0.0.0/0'

/** The IPv6 range that admits every source. */
const ANY_IPV6_SOURCE = '::/0'

/**
 * Whether a source range admits every source rather than restricting the rule to some of them.
 *
 * This is the whole of what the firewall screen can know about the lockout risk of a rule. A TCP
 * rule scoped to a NARROWER range than everything is the only kind of addition that can displace
 * the unconditional accept the agent renders for the host's SSH ports, and thereby cut the operator
 * off; an allow open to everyone replaces that accept with an identical one and costs nothing.
 * Which port SSH actually listens on is a host fact the panel holds and never sends to the browser,
 * so the screen reasons about the source range instead — and errs towards asking.
 * @param cidr The source range, as the panel reported it or as the operator typed it.
 * @returns True when the range admits every source.
 */
export const isAnySourceRange = (cidr: string): boolean => {
  return cidr === ANY_IPV4_SOURCE || cidr === ANY_IPV6_SOURCE
}
