/**
 * The hosting accounts one login may act for, and the switch between them (rules/vue.md "Types").
 * One person can own several accounts; the session speaks for exactly one at a time, and every
 * tenant screen follows that one.
 */

/** One account the signed-in person may switch to, as `GET /api/v1/me/accounts` reports it. */
export interface AccountMembership {
  /** The account's identity, sent back to switch to it. */
  accountId: string
  /** The account's name, shown as the selector's label. */
  name: string
  /** Whether the current sign-in is acting for this account. */
  isCurrent: boolean
}

/** The body of `POST /api/v1/me/active-account`. */
export interface SwitchAccountRequest {
  /** The account to act for. The server refuses (404) one the person holds no membership of. */
  accountId: string
}

/**
 * The answer to a switch. The session is the same one, so there is no refresh token and no new
 * cookie: only a token that names the chosen account.
 */
export interface SwitchedAccount {
  /** The replacement access token. */
  accessToken: string
  /** When it stops being accepted. */
  expiresAt: string
  /** The account the session now acts for. */
  accountId: string
}

/** Public surface of the account memberships API composable. */
export interface AccountMembershipsApi {
  /** Reads the accounts the caller may switch between. */
  list: (signal?: AbortSignal) => Promise<AccountMembership[]>
  /** Makes the caller's sign-in act for another of their accounts. */
  switchTo: (request: SwitchAccountRequest, signal?: AbortSignal) => Promise<SwitchedAccount>
}
