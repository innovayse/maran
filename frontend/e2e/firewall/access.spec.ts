import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall, stubFirewallRefusal } from '../fixtures/stub-firewall-routes'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
]

// A signed-in customer, whose `accountId` is what an administrator does not have. The role guard
// stops this person's navigation to /firewall (advice); the panel refusing the request is the
// boundary.
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000c1',
  username: 'alice',
  email: 'alice@example.com',
  role: 'customer',
  accountId: '22222222-2222-2222-2222-222222222222',
}

/**
 * The panel's own refusal, in the shape `FirewallRulesController` produces it: administrators only,
 * already localized by the backend.
 */
const REFUSAL_DETAIL = 'Administrators only.'

/**
 * Puts a customer in front of the firewall URL, with all three collections refused.
 * @param page The Playwright page under test.
 * @param status The status the panel answers the three collections with.
 * @returns Resolves once the screen has been navigated to.
 */
const openAsCustomer = async (page: Page, status: number): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewallRefusal(page, status, REFUSAL_DETAIL)
  await page.goto('/firewall')
}

// Issue #202: `/firewall` is `adminOnly`, so the role guard sends a customer to the "not available"
// screen before the firewall page mounts. The guard is advice, not the boundary — the panel still
// refuses the request (403, a fact about the whole machine) — so the stubbed refusal stays in place
// and these cases assert what a customer now sees instead of an administrator's screen around it.
test('a customer reaching the firewall URL lands on the not-available screen', async ({ page }) => {
  await openAsCustomer(page, 403)

  await expect(page).toHaveURL(/\/not-available$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
  // Matched as text: `NotFoundPage` renders its title through `UiEmptyState`, a paragraph.
  await expect(page.getByText('Page not found')).toHaveCount(0)
})

// The three lists are the whole screen, so a customer sent away from it must be offered none of its
// controls: no table, and none of the three forms.
test('a customer sent away from the firewall screen is offered no rules, no bans and no exemptions', async ({
  page,
}) => {
  await openAsCustomer(page, 403)

  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
  await expect(page.getByText(REFUSAL_DETAIL)).toHaveCount(0)
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Open the port' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Ban the address' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Add the exemption' })).toHaveCount(0)
})

// The same rendering path carries every failure, not only the authorization one: whatever the panel
// says is what the operator reads.
test('a panel failure on the firewall screen is reported in the panel own words', async ({
  page,
}) => {
  const backendDetail = 'The host firewall is not answering. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewallRefusal(page, 500, backendDetail)

  await page.goto('/firewall')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

// An administrator gets the screen itself, which is the other half of the assertion above: without
// this, a screen that refused everybody would pass every test in this file.
test('an administrator reaching the same URL gets the firewall screen', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })

  await page.goto('/firewall')

  await expect(page.getByRole('heading', { level: 1, name: 'Firewall' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Open the port' })).toBeVisible()
})
