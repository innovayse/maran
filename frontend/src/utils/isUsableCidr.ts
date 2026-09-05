/**
 * What separates an IPv6 address from its scope id. A range carrying one is refused rather than
 * stripped, exactly as the panel's `CidrRange` refuses it: a scope names a link-local address that
 * means different machines on different interfaces, so the range without it is a different range.
 */
const SCOPE_SEPARATOR = '%'

/** A decimal number with no leading zero — the only spelling of an octet or a prefix accepted. */
const DECIMAL = /^(0|[1-9][0-9]*)$/

/** How many octets a dotted-quad has. */
const IPV4_OCTETS = 4

/** The largest value an octet can hold. */
const MAX_OCTET = 255

/** The longest prefix an IPv4 range can carry. */
const MAX_IPV4_PREFIX = 32

/** The longest prefix an IPv6 range can carry. */
const MAX_IPV6_PREFIX = 128

/**
 * Reads a dotted-quad into its four octets.
 *
 * Leading zeros are refused rather than read as octal or as decimal, because the panel's own parser
 * refuses them: `010.1.1.1` is not an address on either side, and accepting it here would produce a
 * field that passes locally and is rejected by the server.
 * @param address The candidate address, without a prefix.
 * @returns The four octets, or `null` when the value is not a dotted-quad.
 */
const toIpv4Octets = (address: string): number[] | null => {
  const parts = address.split('.')
  if (parts.length !== IPV4_OCTETS) {
    return null
  }

  const octets: number[] = []
  for (const part of parts) {
    if (!DECIMAL.test(part)) {
      return null
    }
    const octet = Number(part)
    if (octet > MAX_OCTET) {
      return null
    }
    octets.push(octet)
  }

  return octets
}

/**
 * Whether an IPv4 address carries no bits below its prefix.
 *
 * Host bits are refused rather than masked away, which is the panel's rule and the right one:
 * `203.0.113.7/24` means either one machine or two hundred and fifty-six of them, and silently
 * picking a reading is how an operator opens a port to a neighbourhood they never meant to.
 * @param octets The address's four octets.
 * @param prefix The prefix length, already known to be 0-32.
 * @returns True when every bit below the prefix is zero.
 */
const hasNoIpv4HostBits = (octets: readonly number[], prefix: number): boolean => {
  const value =
    (octets[0] ?? 0) * 2 ** 24 + (octets[1] ?? 0) * 2 ** 16 + (octets[2] ?? 0) * 2 ** 8 + (octets[3] ?? 0)

  // Arithmetic rather than a shift: `value >>> 32` is `value`, not zero, so the /0 case would pass
  // whatever the address was.
  return value % 2 ** (MAX_IPV4_PREFIX - prefix) === 0
}

/**
 * How the browser's URL parser spells the leading five zero groups plus `ffff` that make an address
 * the IPv4-mapped form. The parser always emits the shortest form and always compresses the longest
 * run of zero groups, and in a mapped address that run is the leading five — so
 * `::ffff:198.51.100.10`, `0:0:0:0:0:ffff:198.51.100.10` and `::ffff:c633:640a` all come back
 * spelled exactly this way, which is what makes one prefix test enough.
 */
const IPV4_MAPPED_PREFIX = '[::ffff:'

/**
 * Reads a string as an IPv6 address and returns the browser's own normalized spelling of it.
 *
 * The browser's URL parser answers this, because it already contains a conforming IPv6 parser and a
 * hand-written one here would be a second implementation with its own disagreements — over `::`,
 * over an embedded dotted-quad, over the shortest-form rules. An invalid host makes the constructor
 * throw, which is the whole of the check.
 * @param address The candidate address, without a prefix.
 * @returns The normalized host in brackets, or `null` when it is not an IPv6 address.
 */
const toIpv6Host = (address: string): string | null => {
  try {
    const host = new URL(`http://[${address}]/`).hostname
    return host.startsWith('[') ? host : null
  } catch {
    return null
  }
}

/**
 * Whether a string is a range the panel will store or send — the client-side mirror of the module's
 * `CidrRange.IsUsable`.
 *
 * This is advice that saves a round trip, never a decision. The panel re-validates every range, and
 * its already-localized refusal is what the operator reads when the two disagree (rules/vue.md).
 *
 * The mirror is complete for IPv4 and deliberately partial for IPv6: the shape and the prefix are
 * checked, the host bits below the prefix are not, because expanding a compressed IPv6 address to
 * compare its low bits is a second parser to get wrong for a case the server refuses anyway. The
 * error direction is the safe one — the client accepts a little more than the server, so nothing is
 * refused here that the panel would have taken.
 *
 * The IPv4-mapped form (`::ffff:198.51.100.10/128`) is the one IPv6 case that IS mirrored, because
 * the panel refuses it: an exemption written that way stays in the IPv6 family while every address
 * the panel compares against it has already been mapped down to plain IPv4, so the row is stored,
 * read back to the administrator verbatim, and matches nobody. Refused rather than rewritten to
 * `198.51.100.10/32` for the panel's own reason — rewriting would have to translate the prefix too
 * (`::ffff:0:0/96` is every IPv4 address), and this file's rule about host bits already settles
 * which way to err.
 * @param cidr The candidate, in CIDR notation.
 * @returns True when the range is one the panel is likely to accept.
 */
export const isUsableCidr = (cidr: string): boolean => {
  // Checked on the text, before anything parses it: a parser is what makes a named scope disappear,
  // so a check afterwards would pass exactly the input this refusal exists for.
  if (cidr.includes(SCOPE_SEPARATOR)) {
    return false
  }

  const parts = cidr.split('/')
  if (parts.length !== 2) {
    return false
  }

  const address = parts[0] ?? ''
  const prefixText = parts[1] ?? ''
  if (!DECIMAL.test(prefixText)) {
    return false
  }

  const prefix = Number(prefixText)
  const octets = toIpv4Octets(address)
  if (octets !== null) {
    return prefix <= MAX_IPV4_PREFIX && hasNoIpv4HostBits(octets, prefix)
  }

  const host = toIpv6Host(address)
  if (host === null) {
    return false
  }

  // Tested on the PARSED spelling, unlike the scope id above: the mapped form has several spellings
  // and only the parse tells them apart.
  return prefix <= MAX_IPV6_PREFIX && !host.startsWith(IPV4_MAPPED_PREFIX)
}
