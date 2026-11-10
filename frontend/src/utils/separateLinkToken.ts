/**
 * The two screens a mailed link opens with a one-time token in the query string. Their addresses are
 * the router's (`router/index.ts`); they are repeated here because this runs before the router has
 * installed, which is the point of it.
 */
const TOKEN_BEARING_PATHS: readonly string[] = ['/reset-password', '/accept-invitation']

/** What a mailed link's address is split into. */
export interface SeparatedLinkToken {
  /** The one-time token the link carried. */
  token: string
  /** The same address without the token: path, any other query parameter and the fragment. */
  address: string
}

/**
 * Splits a one-time token out of the address of a mailed link.
 *
 * A token left in the address is sent as the `Referer` of every request the page makes and kept in
 * the browser's history, so the caller removes it from the address before anything else reads the
 * address. Only the two token-bearing screens are touched; every other address, and a link whose
 * `token` is absent or empty, answers `null` and is left exactly as it is.
 * @param pathname The address's path, such as `/reset-password`.
 * @param search The address's query string with its leading `?`, or the empty string.
 * @param hash The address's fragment with its leading `#`, or the empty string.
 * @returns The token and the address without it, or `null` when there is nothing to separate.
 */
export const separateLinkToken = (pathname: string, search: string, hash: string): SeparatedLinkToken | null => {
  if (!TOKEN_BEARING_PATHS.includes(pathname)) {
    return null
  }
  const parameters = new URLSearchParams(search)
  const token = parameters.get('token')
  if (token === null || token === '') {
    return null
  }
  parameters.delete('token')
  const remaining = parameters.toString()
  return { token, address: `${pathname}${remaining === '' ? '' : `?${remaining}`}${hash}` }
}
