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

// A signed-in customer, whose `accountId` is what an administrator does not have. The module
// catalogue is anonymous and role-blind, so this person's navigation to /firewall is not stopped by
// any guard — what stops them is the panel, on the request.
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

// The status is `403` and not the `404` a tenant-scoped resource answers with, and that is the
// module's deliberate choice: `FirewallRulesController` says so in as many words — a firewall rule
// is a fact about the whole machine, its existence discloses nothing about any customer, and there
// is no tenant here to hide behind. So there is nothing for the SPA to shape into a 404, and this
// spec asserts what the module actually does.
test('a customer reaching the firewall URL is shown the panel own refusal, verbatim', async ({
  page,
}) => {
  await openAsCustomer(page, 403)

  await expect(page.getByText(REFUSAL_DETAIL)).toBeVisible()
})

// rules/vue.md: the frontend holds no text for a server outcome, and no route guard duplicates an
// authorization decision the server has already made. A screen that invented its own "not found"
// for a refused request would be a second copy of that decision — and the copy that cannot be
// trusted.
test('the refused firewall screen invents no not-found page of its own', async ({ page }) => {
  await openAsCustomer(page, 403)

  await expect(page.getByText(REFUSAL_DETAIL)).toBeVisible()
  // Matched as text rather than as a heading: `NotFoundPage` renders its title through
  // `UiEmptyState`, which is a paragraph — a heading query would pass here even if the SPA had
  // routed the customer to the 404 screen, which is the assertion failing to assert anything.
  await expect(page.getByText('Page not found')).toHaveCount(0)
  await expect(page.getByText('The page you requested does not exist.')).toHaveCount(0)
  await expect(page).toHaveURL(/\/firewall$/)
})

// The three lists are the whole screen, so a refusal has to take the controls with it: a form left
// on screen beside the refusal invites an operator to type a rule that cannot be sent.
test('a refused firewall screen offers no rules, no bans and no exemptions to act on', async ({
  page,
}) => {
  await openAsCustomer(page, 403)

  await expect(page.getByText(REFUSAL_DETAIL)).toBeVisible()
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
