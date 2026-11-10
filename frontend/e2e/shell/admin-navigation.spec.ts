import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubQuietOwnAccount } from '../fixtures/stub-quiet-own-account'
import { stubEmptySearch } from '../fixtures/stub-search-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Page } from '@playwright/test'

// Issue #311: the administrator's menu is one task-grouped tree. Every built page is a link, every
// page not built yet is drawn disabled with a "coming soon" hint, pages keep their URLs while the
// menu groups them by task, and the jump-to palette offers built pages only. Navigation is by
// clicks only.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The administrator's top-level items, in drawing order. */
const ADMIN_ITEMS = [
  'System status',
  'Accounts',
  'Sites and domains',
  'DNS',
  'Mail',
  'Databases',
  'Files and access',
  'Backups',
  'Security',
  'Servers',
  'Monitoring',
  'Settings',
]

/**
 * The names of the top-level rows, links and disabled rows alike, in document order.
 * @param page The Playwright page under test.
 * @returns Each row's accessible name.
 */
const topLevelNames = async (page: Page): Promise<string[]> => {
  return mainNavigation(page)
    .locator('ul[aria-labelledby] > li')
    .evaluateAll((rows): string[] => {
      return rows.map((row): string => {
        // The row's own label cluster: the first link or disabled row, before any submenu.
        return row.querySelector('a, [role="link"]')?.firstElementChild?.textContent?.trim() ?? ''
      })
    })
}

test('an administrator sees the twelve task-grouped items in order', async ({ page }) => {
  await openFullShell(page)
  // Polled: the panel's own items are drawn before the module catalogue arrives.
  await expect.poll(async (): Promise<string[]> => {
    return topLevelNames(page)
  }).toEqual(ADMIN_ITEMS)
})

test('a page not built yet is shown disabled, with a coming-soon hint and no link', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  // A planned page inside a usable section sits beside its built siblings.
  await openSection(page, 'Sites and domains')
  await expect(nav.getByRole('link', { name: 'Domains and aliases Coming soon' })).toHaveAttribute('aria-disabled', 'true')
  // `force` because Playwright refuses to click an aria-disabled element, and the click is the point:
  // a real pointer press on the disabled row must go nowhere.
  await nav.getByRole('link', { name: 'Domains and aliases Coming soon' }).click({ force: true })
  await expect(page).toHaveURL('/')
})

// Issue #179: the reseller screens landed, so the Resellers row is a routed link and no longer a
// disabled "coming soon" one. The same row is the control that proves the disabled form above is
// still told apart from a built page.
test('the Resellers page is a built link under Accounts and opens the reseller list', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  await openSection(page, 'Accounts')
  await expect(nav.getByRole('link', { name: 'Resellers Coming soon' })).toHaveCount(0)
  await nav.getByRole('link', { name: 'Resellers', exact: true }).click()

  await expect(page).toHaveURL('/resellers')
})

test('the audit journal sits under Monitoring and keeps its settings address', async ({ page }) => {
  await openFullShell(page)
  await openSection(page, 'Monitoring')
  await mainNavigation(page).getByRole('link', { name: 'Audit journal', exact: true }).click()
  await expect(page).toHaveURL('/settings/audit')
})

test('the security policy sits under Security and keeps its settings address', async ({ page }) => {
  await openFullShell(page)
  await openSection(page, 'Security')
  await mainNavigation(page).getByRole('link', { name: 'Security policy', exact: true }).click()
  await expect(page).toHaveURL('/settings/security-policy')
})

test('the jump-to palette offers built pages and leaves planned ones out', async ({ page }) => {
  await openFullShell(page)
  // Typing into the palette asks the panel's search too; it finds nothing, the pages are the subject.
  await stubEmptySearch(page)
  await page.keyboard.press('Control+k')
  const field = page.getByRole('combobox', { name: 'Search the panel' })
  await expect(field).toBeFocused()

  // A page not built yet (DNS zones were here until issue #178 built them).
  await field.fill('Domains and aliases')
  await expect(page.getByRole('option')).toHaveCount(0)

  await field.fill('Bans')
  await page.getByRole('option', { name: 'Bans' }).click()
  await expect(page).toHaveURL('/firewall/bans')
})

// The customer's own planned items (mail) are theirs too (issue #312) and are asserted in
// `customer/customer-navigation.spec.ts`; only what belongs to the server is absent here.
test('a customer sees none of the server items, built or planned', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubShellReads(page)
  await stubLandingReads(page, 'customer')
  await stubQuietOwnAccount(page, CUSTOMER.accountId ?? '')
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')
  const nav = mainNavigation(page)
  await expect(nav.getByRole('link', { name: 'System status' })).toBeVisible()

  for (const name of ['Accounts', 'Security', 'Servers', 'Monitoring', 'Settings']) {
    await expect(nav.getByRole('link', { name, exact: true })).toHaveCount(0)
  }
  await expect(nav.getByRole('button', { name: 'Show the Security pages' })).toHaveCount(0)
})

test('on a phone the drawer opens each section as an accordion', async ({ page }) => {
  await openFullShell(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'Open the navigation' }).click()

  const drawer = page.getByRole('dialog')
  const toggle = drawer.getByRole('button', { name: 'Show the Security pages' })
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await drawer.getByRole('link', { name: 'Bans', exact: true }).click()

  await expect(page).toHaveURL('/firewall/bans')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('the background tasks item shows how many tasks failed', async ({ page }) => {
  await openFullShell(page)
  // Installed after the shell's catch-all so it wins; the shell reads the count once, on load.
  await page.route(/\/api\/v1\/tasks\/failed-count(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '3' })
  })
  await page.reload()
  await openSection(page, 'Monitoring')
  await expect(mainNavigation(page).getByRole('link', { name: 'Background tasks 3 failed' })).toBeVisible()
})
