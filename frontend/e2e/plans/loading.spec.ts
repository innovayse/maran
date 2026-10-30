import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubManagedPlans } from '../fixtures/stub-plans-route'
import type { PanelModule } from '../../src/types/module'

// The plans table while the list is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }]

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The plans could not be read.'

/**
 * Signs in with the accounts module licensed.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

describeTableLoading('plans list', {
  stub: async (page) => {
    await signIn(page)
    await stubManagedPlans(page)
  },
  stubFailure: async (page) => {
    await signIn(page)
    await page.route('**/api/v1/plans', async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: '**/api/v1/plans',
  path: '/plans',
  table: 'Plans and their limits',
  rowText: 'Starter',
  failureText: REFUSAL,
})
