/**
 * Shape checks for the addresses and host names the DNS screens ask for. They mirror the server's
 * validators as advice that saves a round trip; the server parses everything again and its
 * localized refusal is what a person reads when the two disagree.
 */

/** The server's longest host name, in characters. */
const MAX_NAME_LENGTH = 253

/** The most an IPv4 octet holds. */
const MAX_OCTET = 255

/** A dotted-quad IPv4 address; each octet is range-checked separately. */
const IPV4_PATTERN = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/

/** The characters an IPv6 address is written with, the structural check being the colons. */
const IPV6_PATTERN = /^[0-9A-Fa-f:.]{2,45}$/

/** A host name: labels of letters, digits, `-` and `_`, never starting or ending with `-`, optionally absolute. */
const HOSTNAME_PATTERN = /^(?!-)[A-Za-z0-9_-]{1,63}(?<!-)(?:\.(?!-)[A-Za-z0-9_-]{1,63}(?<!-))*\.?$/

/**
 * Whether a string is an IPv4 address.
 * @param text The candidate.
 * @returns True when it has four octets of 0-255.
 */
export const isIpv4 = (text: string): boolean => {
  const match = IPV4_PATTERN.exec(text)
  return (
    match !== null &&
    match.slice(1).every((octet): boolean => {
      return Number(octet) <= MAX_OCTET
    })
  )
}

/**
 * Whether a string looks like an IPv6 address. A shape check only: the server parses it for real.
 * @param text The candidate.
 * @returns True when it has the characters of an address and at least two colons.
 */
export const isIpv6 = (text: string): boolean => {
  return IPV6_PATTERN.test(text) && text.split(':').length > 2
}

/**
 * Whether a string is a host name a record or a name server may point at.
 * @param text The candidate.
 * @returns True when it is within the length limit and made of valid labels.
 */
export const isHostname = (text: string): boolean => {
  return text.length <= MAX_NAME_LENGTH && HOSTNAME_PATTERN.test(text)
}
