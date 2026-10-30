import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { PanelModule } from '../../src/types/module'

// Issue #209: the collapsed rail and jump-to listed the entries in catalogue order while the
// expanded sidebar drew them by group, and two entries were named for their module rather than
// for the screen they open.

/** A catalogue in an order deliberately unlike the sidebar's groups. */
const MODULES: PanelModule[] = [
  { name: 'identity', displayName: 'Users and access', tier: 'included', isEnabled: true },
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'notifications', displayName: 'Notifications', tier: 'included', isEnabled: true },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  { name: 'tasks', displayName: 'Tasks', tier: 'included', isEnabled: true },
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
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

  await expect(nav.getByRole('link', { name: 'Signed-in devices' })).toHaveAttribute('href', '/settings/sessions')
  await expect(nav.getByRole('link', { name: 'Outgoing mail' })).toHaveAttribute('href', '/settings/smtp')
})
