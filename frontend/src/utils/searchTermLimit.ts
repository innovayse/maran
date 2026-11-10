/**
 * The longest search term, in characters, a list accepts. It mirrors the backend's
 * `PageDefaults.SearchMaxLength`: the server stays the authority and refuses a longer term, so the
 * field says so first and no request is spent on a term that is certain to be refused.
 */
export const SEARCH_TERM_MAX_LENGTH = 100

/**
 * Whether a typed search is longer than the server accepts. The server trims before it measures, so
 * the same trim is applied here.
 * @param text The text typed into a search field.
 * @returns True when the server would refuse the term for its length.
 */
export const isSearchTermTooLong = (text: string): boolean => {
  return text.trim().length > SEARCH_TERM_MAX_LENGTH
}
