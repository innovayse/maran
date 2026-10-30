import type { Page } from '@playwright/test'
import type { MyAccount } from '../../src/types/account'

/** The customer's own account endpoint. */
const MY_ACCOUNT_PATTERN = /\/api\/v1\/accounts\/me(\?.*)?$/

/**
 * The administrator-only account reads: the paged list and the picker choices, matched exactly so
 * `/accounts/me` is not caught by it.
 */
const ACCOUNTS_LIST_PATTERN = /\/api\/v1\/accounts(\/choices)?(\?.*)?$/

/**
 * Fulfils `GET /api/v1/accounts/me` with the given account, as the panel answers a customer.
 * @param page The Playwright page whose network the route is installed on.
 * @param account The account the stub reports as the caller's own.
 * @returns Resolves once the route is installed.
 */
export const stubMyAccount = async (page: Page, account: MyAccount): Promise<void> => {
  await page.route(MY_ACCOUNT_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(account) })
  })
}

/**
 * Answers any request to the administrator-only `GET /api/v1/accounts` list or its choices with 403
 * and counts it, so a customer spec can assert the SPA never asked. Counting rather than throwing inside the
 * handler: an exception in a route handler does not fail the test that installed it.
 * @param page The Playwright page whose network the route is installed on.
 * @returns A function reporting how many times the list was requested so far.
 */
export const forbidAccountsList = async (page: Page): Promise<() => number> => {
  let calls = 0
  await page.route(ACCOUNTS_LIST_PATTERN, async (route) => {
    calls += 1
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostForbidden', title: 'Forbidden', detail: 'Forbidden' }),
    })
  })
  return () => {
    return calls
  }
}
