import type { Page } from '@playwright/test'
import type { Account } from '../../src/types/account'

/** The endpoint the accounts list and the create-account form both talk to. */
const ACCOUNTS_PATTERN = '**/api/v1/accounts'

/**
 * Fulfils `GET /api/v1/accounts` with the given list, and answers a `POST`
 * to the same endpoint by echoing the submitted body back as a created
 * account. Lets a spec drive the list page's empty/populated states and the
 * form's success path without a live backend.
 * @param page The Playwright page whose network the route is installed on.
 * @param accounts The accounts the stub reports for the list request.
 * @returns Resolves once the route is installed.
 */
export const stubAccounts = async (page: Page, accounts: Account[]): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as Pick<Account, 'name' | 'primaryDomain' | 'planId'>
      const created: Account = {
        id: '11111111-1111-1111-1111-111111111111',
        name: submitted.name,
        primaryDomain: submitted.primaryDomain,
        planId: submitted.planId,
        status: 'active',
        createdAt: '2026-08-29T10:00:00Z',
      }
      // The list request that follows the form's redirect must show the new row, so the created
      // account joins the catalogue this stub serves from then on.
      accounts.push(created)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(accounts),
    })
  })
}

/**
 * Fulfils `GET /api/v1/accounts` with an RFC 7807 problem body, so a spec can
 * assert the list page renders the backend's own already-localized message
 * verbatim (rules/vue.md: "the backend owns their text").
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param code The machine-stable problem code the stub reports.
 * @param status HTTP status code the stub responds with.
 * @returns Resolves once the route is installed.
 */
export const stubAccountsProblem = async (
  page: Page,
  detail: string,
  code = 'HostUnexpectedError',
  status = 500,
): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code, title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/accounts` list but rejects `POST` with an
 * RFC 7807 problem body, so a spec can assert the create form surfaces the
 * server's own message rather than frontend-invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param code The machine-stable problem code the stub reports.
 * @param status HTTP status code the stub responds with.
 * @returns Resolves once the route is installed.
 */
export const stubCreateAccountProblem = async (
  page: Page,
  detail: string,
  code = 'AccountNameTaken',
  status = 409,
): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code, title: 'Conflict', detail }),
      })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}
