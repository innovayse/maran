import { expect, test, type Page } from '@playwright/test'
import { FULL_CATALOGUE, mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { AuthenticatedUser } from '../../src/types/auth'

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

  const dns = nav.getByRole('link', { name: 'DNS Coming soon' })
  await expect(dns).toHaveAttribute('aria-disabled', 'true')
  await expect(dns).toHaveAttribute('title', 'Coming soon')
  await expect(dns).not.toHaveAttribute('href', /.*/)

  // A planned page inside a usable section sits beside its built siblings.
  await openSection(page, 'Accounts')
  await expect(nav.getByRole('link', { name: 'Resellers Coming soon' })).toHaveAttribute('aria-disabled', 'true')
  // `force` because Playwright refuses to click an aria-disabled element, and the click is the point:
  // a real pointer press on the disabled row must go nowhere.
  await nav.getByRole('link', { name: 'Resellers Coming soon' }).click({ force: true })
  await expect(page).toHaveURL('/')
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
  await page.keyboard.press('Control+k')
  const field = page.getByRole('combobox', { name: 'Search the panel' })
  await expect(field).toBeFocused()

  await field.fill('Zones')
  await expect(page.getByRole('option')).toHaveCount(0)

  await field.fill('Bans')
  await page.getByRole('option', { name: 'Bans' }).click()
  await expect(page).toHaveURL('/firewall/bans')
})

test('a customer sees none of the server items, built or planned', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')
  const nav = mainNavigation(page)
  await expect(nav.getByRole('link', { name: 'System status' })).toBeVisible()

  for (const name of ['Security', 'Servers', 'DNS Coming soon', 'Mail Coming soon', 'Settings']) {
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
