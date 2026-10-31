import { expect, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubModules } from './stub-modules-route'
import type { PanelModule } from '../../src/types/module'

/**
 * Every module the panel composes, licensed and enabled, under the names the panel reports. The
 * navigation specs of issue #209 walk the whole sidebar, so they need every entry it can draw.
 */
export const FULL_CATALOGUE: PanelModule[] = [
  { name: 'identity', displayName: 'Users and access', tier: 'included', isEnabled: true },
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
  { name: 'ssl', displayName: 'SSL certificates', tier: 'included', isEnabled: true },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
  { name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true },
  { name: 'tasks', displayName: 'Tasks', tier: 'included', isEnabled: true },
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
  { name: 'notifications', displayName: 'Notifications', tier: 'included', isEnabled: true },
]

/**
 * Signs in as an administrator on a desktop with {@link FULL_CATALOGUE} and opens a path.
 *
 * Every `/api/v1` request no narrower stub answers gets an empty 404 problem, installed first so
 * any stub a spec adds afterwards wins Playwright's ordering. These specs are about the shell
 * (sidebar, breadcrumb, eyebrow), so a screen's own content may be a refusal; its heading and the
 * shell around it are drawn either way.
 * @param page The Playwright page under test.
 * @param path Where to open.
 * @returns Resolves once the main navigation is drawn.
 */
export const openFullShell = async (page: Page, path = '/'): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.route(/\/api\/v1\//, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Not found', status: 404 }),
    })
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(path)
  // The sidebar's own section rows, located without its translated name so any language opens.
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

/**
 * The sidebar's navigation landmark.
 * @param page The Playwright page under test.
 * @returns Its locator.
 */
export const mainNavigation = (page: Page): Locator => {
  return page.getByRole('navigation', { name: 'Main navigation' })
}

/**
 * Opens a sidebar section's submenu by its toggle, unless it is already open.
 * @param page The Playwright page under test.
 * @param section The section's name, as the toggle's "Show the … pages" names it.
 * @returns Resolves once the submenu is open.
 */
export const openSection = async (page: Page, section: string): Promise<void> => {
  const toggle = mainNavigation(page).getByRole('button', { name: `Show the ${section} pages` })
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
    await toggle.click()
  }
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
}

/**
 * Reads the app bar's breadcrumb, crumb by crumb.
 * @param page The Playwright page under test.
 * @returns Its locator's list items.
 */
export const breadcrumbItems = (page: Page): Locator => {
  return page.getByRole('navigation', { name: 'Breadcrumb' }).getByRole('listitem')
}
