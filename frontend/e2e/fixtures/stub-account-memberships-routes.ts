import type { Page } from '@playwright/test'
import type { AccountMembership, SwitchAccountRequest, SwitchedAccount } from '../../src/types/accountMembership'
import type { AuthenticatedUser } from '../../src/types/auth'

/** The two `me` endpoints the account selector talks to. */
const ACCOUNTS_PATTERN = /\/api\/v1\/me\/accounts(\?.*)?$/
const ACTIVE_ACCOUNT_PATTERN = /\/api\/v1\/me\/active-account(\?.*)?$/

/**
 * What the stubbed panel holds for one person who may act for several accounts. Shared between
 * the routes on purpose: the switch changes what the list, and the refresh that follows the
 * panel's reload, report.
 */
export interface MembershipsState {
  /** The accounts the person may act for, in name order. */
  accounts: { accountId: string; name: string }[]
  /** The account the session currently acts for. */
  activeAccountId: string
  /** When set, the next switch is refused with this problem instead of being made. */
  refuseSwitchWith?: { status: number; detail: string }
  /** Every body the panel received on the switch endpoint, in order. */
  switchRequests: SwitchAccountRequest[]
}

/**
 * Builds the answer `GET /api/v1/me/accounts` gives from the held state.
 * @param state What the panel holds.
 * @returns One row per account with the active one marked.
 */
const rows = (state: MembershipsState): AccountMembership[] => {
  return state.accounts.map((account): AccountMembership => {
    return { ...account, isCurrent: account.accountId === state.activeAccountId }
  })
}

/**
 * Fulfils `GET /api/v1/me/accounts` and `POST /api/v1/me/active-account` from an in-memory state the
 * way the panel does: the list marks the active account, a switch to an account the person holds
 * changes it and answers with a token naming it, and one to any other account is a 404.
 * @param page The Playwright page whose network the routes are installed on.
 * @param state What the panel holds; mutated by switches.
 * @returns Resolves once the routes are installed.
 */
export const stubAccountMemberships = async (page: Page, state: MembershipsState): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(rows(state)) })
  })

  await page.route(ACTIVE_ACCOUNT_PATTERN, async (route) => {
    const request = route.request().postDataJSON() as SwitchAccountRequest
    state.switchRequests.push(request)

    const refuse = state.refuseSwitchWith
    const known = state.accounts.some((account): boolean => {
      return account.accountId === request.accountId
    })
    if (refuse !== undefined || !known) {
      await route.fulfill({
        status: refuse?.status ?? 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          title: refuse?.detail ?? 'This account could not be found.',
          detail: refuse?.detail ?? 'This account could not be found.',
          status: refuse?.status ?? 404,
          code: 'AccountMembershipNotFound',
        }),
      })
      return
    }

    state.activeAccountId = request.accountId
    const switched: SwitchedAccount = {
      accessToken: 'stub.access.token',
      expiresAt: '2099-01-01T00:00:00+00:00',
      accountId: request.accountId,
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(switched) })
  })
}

/**
 * Makes the stubbed refresh answer the way the panel does after a switch: the same session, now
 * naming the account that was chosen. Installed AFTER `stubSignedIn`, because Playwright matches
 * routes newest-first.
 * @param page The Playwright page whose network the route is installed on.
 * @param user The signed-in customer; its `accountId` is replaced by the state's active account.
 * @param state What the panel holds.
 * @returns Resolves once the route is installed.
 */
export const stubRefreshFollowsActiveAccount = async (
  page: Page,
  user: AuthenticatedUser,
  state: MembershipsState,
): Promise<void> => {
  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'stub.access.token',
        expiresAt: '2099-01-01T00:00:00+00:00',
        user: { ...user, accountId: state.activeAccountId },
        requiresTwoFactorSetup: false,
      }),
    })
  })
}
