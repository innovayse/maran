import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { PanelModule } from '../../src/types/module'

// Issue #208: the jump-to palette opened with focus on its Close button, so typing did nothing,
// and the app bar could hold two "Search or jump to…" buttons at once.

/** A catalogue with a module whose name the query below picks out. */
const MODULES: PanelModule[] = [
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

/**
 * Signs in and opens the landing screen at a width.
 * @param page The Playwright page under test.
 * @param width The viewport width in CSS pixels.
 * @returns Resolves once the shell is drawn.
 */
const open = async (page: Page, width: number): Promise<void> => {
  await page.setViewportSize({ width, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  await page.goto('/')
  await expect(page.getByRole('banner')).toBeVisible()
}

/**
 * Types into the palette and asserts focus and filtering.
 * @param page The Playwright page under test.
 * @returns Resolves once the assertions hold.
 */
const typeAndExpectFiltered = async (page: Page): Promise<void> => {
  const dialog = page.getByRole('dialog', { name: 'Jump to' })
  await expect(dialog).toBeVisible()
  const field = dialog.getByRole('combobox', { name: 'Search the panel' })
  await expect(field).toBeFocused()

  await page.keyboard.type('fire')
  await expect(field).toHaveValue('fire')
  const options = dialog.getByRole('option')
  await expect(options).toHaveCount(1)
  await expect(options.first()).toHaveText(/Firewall/)
}

for (const width of [1440, 390]) {
  test(`at ${width}px Ctrl+K opens the palette with focus in its search field`, async ({ page }) => {
    await open(page, width)
    await page.keyboard.press('Control+k')
    await typeAndExpectFiltered(page)
  })

  test(`at ${width}px the search button opens the palette with focus in its search field`, async ({
    page,
  }) => {
    await open(page, width)
    await page.getByRole('banner').getByRole('button', { name: 'Search or jump to…' }).click()
    await typeAndExpectFiltered(page)
  })

  test(`at ${width}px exactly one search control is in the accessibility tree`, async ({ page }) => {
    await open(page, width)
    // `getByRole` skips `display: none`, so the count is what assistive technology is offered.
    await expect(page.getByRole('button', { name: 'Search or jump to…' })).toHaveCount(1)
  })
}
