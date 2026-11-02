import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSites, stubPhpVersions } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'

// Sidebar submenus (issue #195): a module with several pages is a collapsible section whose
// toggle carries aria-expanded, opens itself on a deep link, is keyboard-operable, and stays
// reachable from the collapsed rail (flyout) and the phone drawer.

/**
 * Catalogue drawing several multi-page sections (Accounts, Sites and domains, Security,
 * Monitoring) and one section left with a single page: of Files and access only `cron` is listed,
 * so that section is a plain link under its own label.
 */
const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', state: 'enabled' },
  { name: 'accounts', displayName: 'Accounts', tier: 'included', state: 'enabled' },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', state: 'enabled' },
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', state: 'enabled' },
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', state: 'enabled' },
]

/**
 * Signs in as an administrator with {@link MODULES} and opens a path.
 * @param page The Playwright page under test.
 * @param path Where to go.
 * @returns Resolves once navigation finished.
 */
const open = async (page: Page, path: string): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubSites(page, [])
  await stubPhpVersions(page, [])
  await page.goto(path)
}

/** The router's active class as a whole class name, the one the nav row's fill is keyed on. */
const LIT = /(^|\s)router-link-active(\s|$)/

test('a section toggles open and closed and says so with aria-expanded', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const toggle = nav.getByRole('button', { name: 'Show the Sites and domains pages' })

  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toHaveCount(0)

  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await expect(nav.getByRole('link', { name: 'Site list' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toBeVisible()

  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toHaveCount(0)

  // A section left with a single page stays a plain link with no toggle.
  await expect(nav.getByRole('link', { name: 'Files and access', exact: true })).toBeVisible()
  await expect(nav.getByRole('button', { name: 'Show the Files and access pages' })).toHaveCount(0)
})

// The firewall is three pages (#196): the rules, and the two server-paged lists that each keep their
// page, search and sort in the URL. The Security section (#311) offers all three, the rules first,
// each named by the title of the page it opens (#209), so the whitelist entry reads "Never banned".
test('the security section offers the firewall rules, bans and whitelist pages', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const toggle = nav.getByRole('button', { name: 'Show the Security pages' })

  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await expect(nav.getByRole('link', { name: 'Rules', exact: true })).toHaveAttribute('href', '/firewall')
  await expect(nav.getByRole('link', { name: 'Bans', exact: true })).toHaveAttribute('href', '/firewall/bans')
  await expect(nav.getByRole('link', { name: 'Never banned', exact: true })).toHaveAttribute(
    'href',
    '/firewall/whitelist',
  )
})

test('a deep link to the bans page opens the security section and highlights the bans child', async ({
  page,
}) => {
  await open(page, '/firewall/bans')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })

  await expect(nav.getByRole('button', { name: 'Show the Security pages' })).toHaveAttribute(
    'aria-expanded',
    'true',
  )
  await expect(nav.getByRole('link', { name: 'Bans', exact: true })).toHaveAttribute('aria-current', 'page')
  await expect(nav.getByRole('link', { name: 'Rules', exact: true })).not.toHaveClass(LIT)
})

test('a deep link opens its section and highlights the current child', async ({ page }) => {
  await open(page, '/settings/php')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })

  await expect(nav.getByRole('button', { name: 'Show the Sites and domains pages' })).toHaveAttribute(
    'aria-expanded',
    'true',
  )
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toHaveAttribute('aria-current', 'page')
  // Only the current child is lit: "Site list" must stay dark (issue #219), while the section's
  // own row is lit because the page belongs to it. The class is matched as a whole word, because
  // the row's Tailwind utilities spell `[&.router-link-active]` too.
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toHaveClass(LIT)
  await expect(nav.getByRole('link', { name: 'Site list' })).not.toHaveClass(LIT)
  await expect(nav.getByRole('link', { name: 'Sites and domains', exact: true })).toHaveClass(LIT)
  // The other section stays as it was.
  await expect(nav.getByRole('button', { name: 'Show the Accounts pages' })).toHaveAttribute(
    'aria-expanded',
    'false',
  )
})

test('clicking a section link navigates and opens it', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })

  await nav.getByRole('link', { name: 'Accounts', exact: true }).click()
  await expect(page).toHaveURL('/accounts')
  await expect(nav.getByRole('link', { name: 'Plans', exact: true })).toBeVisible()
})

test('the keyboard opens a section, walks into it with arrows and comes back', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const sites = nav.getByRole('link', { name: 'Sites and domains', exact: true })

  await sites.focus()
  await page.keyboard.press('ArrowRight')
  await expect(nav.getByRole('button', { name: 'Show the Sites and domains pages' })).toHaveAttribute(
    'aria-expanded',
    'true',
  )

  await page.keyboard.press('ArrowDown')
  await expect(nav.getByRole('link', { name: 'Site list' })).toBeFocused()
  await page.keyboard.press('ArrowLeft')
  await expect(sites).toBeFocused()
  await page.keyboard.press('ArrowLeft')
  await expect(nav.getByRole('link', { name: 'Site list' })).toHaveCount(0)
})

test('the collapsed rail shows a flyout that reaches every child', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 })
  await open(page, '/')
  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('link', { name: 'Sites and domains', exact: true }).hover()
  await expect(nav.getByRole('link', { name: 'PHP versions' })).toBeVisible()

  await nav.getByRole('link', { name: 'PHP versions' }).click()
  await expect(page).toHaveURL('/settings/php')
})

test('the collapsed rail flyout is reached by keyboard and closes on Escape', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 })
  await open(page, '/')
  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const sites = nav.getByRole('link', { name: 'Sites and domains', exact: true })
  await sites.focus()
  await page.keyboard.press('Tab')
  await expect(nav.getByRole('link', { name: 'Site list' })).toBeFocused()

  await page.keyboard.press('Escape')
  await expect(sites).toBeFocused()
  await expect(nav.getByRole('link', { name: 'Site list' })).toHaveCount(0)
})

test('the phone drawer carries the same submenus with 44px rows', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await open(page, '/sites')
  await page.locator('button[aria-label="Open the navigation"]').first().click()

  const drawer = page.getByRole('dialog')
  const child = drawer.getByRole('link', { name: 'PHP versions' })
  await expect(child).toBeVisible()
  const box = await child.boundingBox()
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)

  await child.click()
  await expect(page).toHaveURL('/settings/php')
})
