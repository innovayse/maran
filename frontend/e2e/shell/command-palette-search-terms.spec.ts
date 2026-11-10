import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubEmptySearch } from '../fixtures/stub-search-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

/**
 * Protects the jump-to palette finding a page by the words an operator uses for it (issue #507),
 * not only by its menu label. The menu says "Rules", "Scheduled tasks" and "Outgoing mail"; the
 * subjects typed are "firewall", "cron" and "smtp".
 */

/**
 * The modules the pages under test belong to, all enabled. `files` keeps the section that holds the
 * scheduled tasks a submenu of two pages: a section left with one page is drawn as that one row
 * under the section's own name.
 */
const MODULES: PanelModule[] = ['sites', 'cron', 'files', 'firewall'].map((name) => {
  return { name, displayName: `Module ${name}`, kindDisplayName: 'Core', tier: 'included', state: 'enabled' }
})

/**
 * Opens the palette on a signed-in administrator's panel.
 * @param page The Playwright page to drive.
 * @returns Resolves once the palette dialog is on screen.
 */
const openPalette = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  // Typing into the palette asks the panel's search too; it finds nothing, the page list is the subject.
  await stubEmptySearch(page)

  await page.goto('/')
  await page.getByRole('button', { name: 'Search or jump to…' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
}

for (const [typed, destination] of [
  ['firewall', 'Rules'],
  ['crontab', 'Scheduled tasks'],
  ['smtp', 'Outgoing mail'],
  ['password', 'Security policy'],
  ['ssl', 'Panel certificate'],
] as const) {
  test(`typing "${typed}" in the palette lists the page labelled "${destination}"`, async ({ page }) => {
    await openPalette(page)

    await page.getByRole('dialog').getByRole('combobox').fill(typed)

    await expect(page.getByRole('dialog').getByRole('option', { name: destination })).toBeVisible()
  })
}

test('a word that names no page still lists nothing, so the search words are not a match-all', async ({
  page,
}) => {
  // Inverse control: the extra words widen what is found, they do not make every query succeed.
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('zzqxv')

  await expect(page.getByRole('dialog').getByRole('option')).toHaveCount(0)
  await expect(page.getByRole('dialog').getByText('Nothing matches that.')).toBeVisible()
})

test('a label word still finds its page', async ({ page }) => {
  // Inverse control: matching on the label is unchanged.
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('Security policy')

  await expect(page.getByRole('dialog').getByRole('option', { name: 'Security policy' })).toBeVisible()
})
