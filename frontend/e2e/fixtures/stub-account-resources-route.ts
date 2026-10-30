import type { Page } from '@playwright/test'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'

/**
 * Fulfils `GET /api/v1/accounts/{id}/resource-summary` with the given summary, as the panel answers
 * an administrator.
 * @param page The Playwright page whose network the route is installed on.
 * @param summary The summary the stub reports; its `accountId` names the URL it answers.
 * @returns Resolves once the route is installed.
 */
export const stubAccountResources = async (page: Page, summary: AccountResourceSummary): Promise<void> => {
  await page.route(`**/api/v1/accounts/${summary.accountId}/resource-summary`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(summary) })
  })
}

/**
 * Fulfils `GET /api/v1/accounts/me/resource-summary` with the given summary, as the panel answers
 * the customer who owns the account.
 * @param page The Playwright page whose network the route is installed on.
 * @param summary The summary the stub reports as the caller's own.
 * @returns Resolves once the route is installed.
 */
export const stubMyAccountResources = async (page: Page, summary: AccountResourceSummary): Promise<void> => {
  await page.route('**/api/v1/accounts/me/resource-summary', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(summary) })
  })
}
