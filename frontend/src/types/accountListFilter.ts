/**
 * Narrows a module's list to the rows one account owns, sent as `?accountId=` on the list request.
 *
 * An empty object lists every row the caller may see. The server ANDs the account with its own
 * tenant filter, so a customer naming another account receives an empty list, never its rows: the
 * filter is a view, not an access decision (rules/security.md).
 */
export interface AccountListFilter {
  /** The account whose rows to list; absent to list every row the caller may see. */
  accountId?: string
}
