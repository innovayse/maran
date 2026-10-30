import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSites } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'

// Sidebar submenus (issue #195): a module with several pages is a collapsible section whose
// toggle carries aria-expanded, opens itself on a deep link, is keyboard-operable, and stays
// reachable from the collapsed rail (flyout) and the phone drawer.

/** Catalogue with two multi-page modules and a single-page one. */
const MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
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
  await page.goto(path)
}

test('a section toggles open and closed and says so with aria-expanded', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const toggle = nav.getByRole('button', { name: 'Show the Sites pages' })

  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(nav.getByRole('link', { name: 'New site' })).toHaveCount(0)

  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await expect(nav.getByRole('link', { name: 'Site list' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'New site' })).toBeVisible()

  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(nav.getByRole('link', { name: 'New site' })).toHaveCount(0)

  // A single-page module stays a plain link with no toggle.
  await expect(nav.getByRole('button', { name: 'Show the Firewall pages' })).toHaveCount(0)
})

test('a deep link opens its section and highlights the current child', async ({ page }) => {
  await open(page, '/sites/new')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })

  await expect(nav.getByRole('button', { name: 'Show the Sites pages' })).toHaveAttribute(
    'aria-expanded',
    'true',
  )
  await expect(nav.getByRole('link', { name: 'New site' })).toHaveAttribute('aria-current', 'page')
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
  await expect(nav.getByRole('link', { name: 'Plans' })).toBeVisible()
})

test('the keyboard opens a section, walks into it with arrows and comes back', async ({ page }) => {
  await open(page, '/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const sites = nav.getByRole('link', { name: 'Sites', exact: true })

  await sites.focus()
  await page.keyboard.press('ArrowRight')
  await expect(nav.getByRole('button', { name: 'Show the Sites pages' })).toHaveAttribute(
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
  await nav.getByRole('link', { name: 'Sites', exact: true }).hover()
  await expect(nav.getByRole('link', { name: 'New site' })).toBeVisible()

  await nav.getByRole('link', { name: 'New site' }).click()
  await expect(page).toHaveURL('/sites/new')
})

test('the collapsed rail flyout is reached by keyboard and closes on Escape', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 })
  await open(page, '/')
  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const sites = nav.getByRole('link', { name: 'Sites', exact: true })
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
  const child = drawer.getByRole('link', { name: 'New site' })
  await expect(child).toBeVisible()
  const box = await child.boundingBox()
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)

  await child.click()
  await expect(page).toHaveURL('/sites/new')
})
