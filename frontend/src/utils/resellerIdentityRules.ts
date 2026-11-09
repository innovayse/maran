/**
 * The longest reseller name the forms let through. It mirrors `ResellerNamePolicy.MaximumLength`
 * (backend, `Accounts/Domain/Policies/ResellerNamePolicy.cs`), which is the authority: change both together.
 */
export const MAX_RESELLER_NAME_LENGTH = 30

/** The longest contact address the forms let through, mirroring the storage bound of an owner address. */
export const MAX_RESELLER_EMAIL_LENGTH = 256

/**
 * A conservative address shape, not a full RFC 5322 grammar: something, an "@", something with a
 * dot. The server's rule is the authority; this only catches the obvious slip before a round trip.
 */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * Whether a reseller's contact address has the shape of an address and fits the storage bound.
 * Advice only: the server re-validates and its answer is what the administrator reads.
 * @param address The address as typed.
 * @returns True when the address is plausible enough to send.
 */
export const isPlausibleResellerEmail = (address: string): boolean => {
  return address.length <= MAX_RESELLER_EMAIL_LENGTH && EMAIL_PATTERN.test(address)
}
