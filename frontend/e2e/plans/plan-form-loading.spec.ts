import type { Page } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { MANAGED_PLANS, stubManagedPlans } from '../fixtures/stub-plans-route'
import type { PanelModule } from '../../src/types/module'

// The plan edit form while the plan is on the way (#243): a stand-in form with the real labels over
// placeholder controls, replaced by the form created from the plan once it is known.

const LICENSED: PanelModule[] = [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }]

/** Starter has accounts on it, so its edit notice is the loaded-only content. */
const STARTER = MANAGED_PLANS[0]

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The plans could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

describeRegionLoading('plan edit form', {
  stub: async (page) => {
    await shell(page)
    await stubManagedPlans(page)
  },
  stubFailure: async (page) => {
    await shell(page)
    await page.route('**/api/v1/plans', async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: '**/api/v1/plans',
  path: `/plans/${STARTER.id}`,
  // The edit notice lands above the fields once the plan is known, so the anchor is the heading
  // above it: the one element both states draw in the same place.
  anchor: (page) => {
    return page.getByRole('heading', { level: 1, name: 'Edit plan' })
  },
  contentText: 'An edit reaches the accounts already on this plan',
  failureText: REFUSAL,
})
