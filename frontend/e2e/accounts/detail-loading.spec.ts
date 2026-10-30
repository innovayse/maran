import { earlySkeletons, recordSkeletonFlashes } from '../fixtures/skeleton-flash'
import { expect, test, type Page } from '@playwright/test'
import { delayRoute } from '../fixtures/delay-route'
import {
  expectRegionFastLoad,
  expectRegionSlowFailure,
  expectRegionSlowLoad,
  type RegionLoadingScenario,
} from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import type { AccountDetail } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'

// The account page while its reads are on the way (#243): the heading and the details card keep
// their real terms over placeholder values, and the resources card is its own region.

const ACCOUNT: AccountDetail = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
  planDisplayName: 'Starter',
  ownerEmail: 'owner@acme.example.com',
}

const SUMMARY: AccountResourceSummary = {
  accountId: ACCOUNT.id,
  resources: [{ kind: 'sites', state: 'counted', used: 2, limitKind: 'limited', limit: 5 }],
}

/** The server's refusal the failure cases answer with. */
const REFUSAL = 'The account could not be read.'

/** The account read. */
const ACCOUNT_PATTERN = `**/api/v1/accounts/${ACCOUNT.id}`

/** The resource-summary read. */
const SUMMARY_PATTERN = `**/api/v1/accounts/${ACCOUNT.id}/resource-summary`

/**
 * Answers one read with a 500 problem carrying {@link REFUSAL}.
 * @param page The Playwright page.
 * @param pattern The read to refuse.
 * @returns Resolves once the route is installed.
 */
const refuse = async (page: Page, pattern: string): Promise<void> => {
  await page.route(pattern, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
    })
  })
}

/**
 * Signs in and stubs everything the account page reads except the account itself.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubShell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubInvitation(page, ACCOUNT.id, 'pending')
}

/**
 * Answers the account read with {@link ACCOUNT}.
 * @param page The Playwright page.
 * @returns Resolves once the route is installed.
 */
const stubAccount = async (page: Page): Promise<void> => {
  await page.route(ACCOUNT_PATTERN, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
}

/** How the account detail screen is opened and answered. */
const ACCOUNT_DETAIL_SCENARIO: RegionLoadingScenario = {
  stub: async (page) => {
    await stubShell(page)
    await stubAccount(page)
    await stubAccountResources(page, SUMMARY)
  },
  stubFailure: async (page) => {
    await stubShell(page)
    await refuse(page, ACCOUNT_PATTERN)
    await stubAccountResources(page, SUMMARY)
  },
  pattern: ACCOUNT_PATTERN,
  path: `/accounts/${ACCOUNT.id}`,
  anchor: (page) => {
    return page.getByRole('term').filter({ hasText: 'Primary domain' })
  },
  contentText: 'owner@acme.example.com',
  failureText: REFUSAL,
}

test('account detail: a slow answer shows placeholders under the real labels, then the content', async ({ page }) => {
  await expectRegionSlowLoad(page, ACCOUNT_DETAIL_SCENARIO)
})

test('account detail: a fast answer shows no placeholder', async ({ page }) => {
  await expectRegionFastLoad(page, ACCOUNT_DETAIL_SCENARIO)
})

test('account detail: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectRegionSlowFailure(page, ACCOUNT_DETAIL_SCENARIO)
})

/**
 * The resources card's own region, told apart from the account's by the card it holds.
 * @param page The Playwright page.
 * @returns The region locator.
 */
const resourcesRegion = (page: Page): ReturnType<Page['getByTestId']> => {
  return page.getByTestId('loading-region').filter({ has: page.getByTestId('account-resources') })
}

test('resources: a slow summary holds a placeholder under the card heading, then the rows', async ({ page }) => {
  await stubShell(page)
  await stubAccount(page)
  await stubAccountResources(page, SUMMARY)
  await delayRoute(page, SUMMARY_PATTERN, 600)
  await page.goto(`/accounts/${ACCOUNT.id}`)

  const region = resourcesRegion(page)
  const heading = region.getByRole('heading', { name: 'Resources' })
  await expect(heading).toBeVisible()
  await expect(region.locator('[data-skeleton]').first()).toBeVisible()
  await expect(region.getByTestId('loading-status')).toHaveText('Loading…')
  const before = await heading.boundingBox()

  await expect(region.getByTestId('resource-sites')).toContainText('2 of 5')
  await expect(region.getByTestId('loading-status')).toHaveText('Loaded')
  await expect(region.locator('[data-skeleton]')).toHaveCount(0)
  expect(await heading.boundingBox()).toEqual(before)
})

test('resources: a fast summary shows no placeholder', async ({ page }) => {
  await stubShell(page)
  await stubAccount(page)
  await stubAccountResources(page, SUMMARY)
  await delayRoute(page, SUMMARY_PATTERN, 50)
  await recordSkeletonFlashes(page)
  await page.goto(`/accounts/${ACCOUNT.id}`)

  const region = resourcesRegion(page)
  // Every frame from the first, for longer than the answer and the 150 ms delay together: no
  // placeholder may be seen in the first 150 ms of its wait, although they are mounted to hold
  // the space (see `recordSkeletonFlashes` for why the wait, not the stub, is the clock).
  await page.waitForTimeout(400)
  expect(await earlySkeletons(page)).toEqual([])
  await expect(region.getByTestId('resource-sites')).toContainText('2 of 5')
})

test('resources: a slow refused summary is the card alert, never a skeleton', async ({ page }) => {
  await stubShell(page)
  await stubAccount(page)
  await refuse(page, SUMMARY_PATTERN)
  await delayRoute(page, SUMMARY_PATTERN, 600)
  await page.goto(`/accounts/${ACCOUNT.id}`)

  const card = page.getByTestId('account-resources')
  await expect(card.getByText(REFUSAL)).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  // The account itself is untouched by the failed count.
  await expect(page.getByText('owner@acme.example.com')).toBeVisible()
})
