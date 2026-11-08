/** Why an address cannot join the recipient list; the screen turns each into its own sentence. */
export type AlertRecipientProblem = 'invalid' | 'duplicate'

/**
 * The shape every deliverable address has: something, an at sign, a host with a dot. The server
 * holds the full rule and re-validates on save, so this only catches a slip before the round trip
 * and errs permissive (rules/vue.md "Forms: the browser never validates").
 */
const ADDRESS_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * Says why an address typed into the recipient editor cannot be added.
 *
 * Addresses compare case-insensitively, as the server stores them lower-cased.
 * @param address The address as typed, untrimmed.
 * @param existing The addresses already in the list.
 * @returns The problem, or `null` when the address may be added.
 */
export const alertRecipientProblem = (address: string, existing: readonly string[]): AlertRecipientProblem | null => {
  const candidate = address.trim().toLowerCase()
  if (!ADDRESS_SHAPE.test(candidate)) {
    return 'invalid'
  }

  return existing.some((entry) => {
    return entry.trim().toLowerCase() === candidate
  })
    ? 'duplicate'
    : null
}
