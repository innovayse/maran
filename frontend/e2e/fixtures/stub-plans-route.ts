import type { Page } from '@playwright/test'
import type { Plan } from '../../src/types/account'

/**
 * The single plan the create-account form offers in specs. One is enough: the form's
 * subject is what it does with a chosen plan, not how a list of them is ordered.
 */
export const THE_PLAN: Plan = {
  id: '22222222-2222-2222-2222-222222222222',
  displayName: 'Starter',
  diskQuotaMb: 5120,
  maxSites: 5,
  maxDatabases: 5,
  maxFtpUsers: 5,
}

/**
 * Fulfils `GET /api/v1/accounts/plans` so the form's plan picker has something to offer.
 *
 * The picker exists because a plan id is server-owned reference data: the form asks the
 * backend what may be chosen rather than letting a person type an identifier
 * (rules/architecture.md).
 * @param page The Playwright page whose network the route is installed on.
 * @param plans The plans the panel reports; the single default plan when omitted.
 * @returns Resolves once the route is installed.
 */
export const stubPlans = async (page: Page, plans: Plan[] = [THE_PLAN]): Promise<void> => {
  await page.route('**/api/v1/accounts/plans', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(plans),
    })
  })
}
