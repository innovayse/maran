import { expect, test } from '../fixtures/guarded-test'
import { openSection } from '../fixtures/open-full-shell'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #209: the collapsed rail and jump-to listed the entries in catalogue order while the
// expanded sidebar drew them by group, and two entries were named for their module rather than
// for the screen they open.

/** A catalogue in an order deliberately unlike the sidebar's groups. */
const MODULES: PanelModule[] = [
  { name: 'identity', displayName: 'Users and access', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'notifications', displayName: 'Notifications', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'tasks', displayName: 'Tasks', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'monitoring', displayName: 'Monitoring', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/**
 * Signs in as an administrator with {@link MODULES} on a desktop.
 * @param page The Playwright page under test.
 * @returns Resolves once the sidebar is drawn.
 */
const open = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await stubShellReads(page)
  await stubLandingReads(page)
  await page.goto('/')
  await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
}

/**
 * Reads the top-level entry names of the navigation, in document order.
 * @param page The Playwright page under test.
 * @returns The accessible names of the links that are not submenu children.
 */
const topLevelNames = async (page: Page): Promise<string[]> => {
  return page
    .getByRole('navigation', { name: 'Main navigation' })
    .locator('a:not([data-nav-parent])')
    .evaluateAll((links) => {
      return links.map((link) => {
        return (link.textContent ?? '').replace(/\s+/g, ' ').trim()
      })
    })
}

test('the expanded sidebar, the collapsed rail and jump-to list the entries in one order', async ({
  page,
}) => {
  await open(page)
  const sidebar = await topLevelNames(page)
  expect(sidebar.length).toBeGreaterThan(3)

  await page.getByRole('banner').getByRole('button', { name: 'Search or jump to…' }).click()
  const palette = await page
    .getByRole('dialog', { name: 'Jump to' })
    .getByRole('option')
    .evaluateAll((options) => {
      return options.map((option) => {
        return (option.textContent ?? '').replace(/\s+/g, ' ').trim()
      })
    })
  // The palette also lists submenu children right after their parent; the top-level order is what
  // has to agree.
  expect(palette.filter((name) => {return sidebar.includes(name)})).toEqual(sidebar)
  await page.keyboard.press('Escape')

  await page.getByRole('button', { name: 'Collapse the sidebar' }).click()
  const rail = await topLevelNames(page)
  expect(rail).toEqual(sidebar)
})

test('entries are named for the screen they open', async ({ page }) => {
  await open(page)
  const nav = page.getByRole('navigation', { name: 'Main navigation' })

  await expect(nav.getByRole('link', { name: 'Users and access' })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Notifications' })).toHaveCount(0)

  // The outgoing-mail screen is named for itself under Settings; the signed-in devices are the
  // person's own screen, reached from the account menu rather than the sidebar.
  await expect(nav.getByRole('link', { name: 'Signed-in devices' })).toHaveCount(0)
  await openSection(page, 'Settings')
  await expect(nav.getByRole('link', { name: 'Outgoing mail', exact: true })).toHaveAttribute('href', '/settings/smtp')
})
