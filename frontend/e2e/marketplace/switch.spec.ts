import { expect, test } from '../fixtures/guarded-test'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { openMarketplace } from '../fixtures/open-marketplace'
import {
  GATED_READ,
  MARKETPLACE_PASSWORD,
  WRONG_PASSWORD_MESSAGE,
  catalogueModule,
  installedModule,
  marketplaceWorld,
  type MarketplaceWorld,
} from '../fixtures/stub-marketplace-routes'
import type { Page } from '@playwright/test'

// Issue #187 and the outbound-door rule (rules/security.md item 10): the marketplace switch is OFF on
// a fresh panel, and while it is off the page asks the panel for its status and nothing else, so the
// panel never contacts the marketplace on an administrator's behalf before the administrator said so.
// Every "nothing was asked" claim below is paired with a positive control on the same recorder: the
// status read IS recorded, and switching the marketplace on makes the catalogue reads appear.

/** A catalogue and an installed list, so a read that wrongly happened would have something to show. */
const stockedWorld = (): MarketplaceWorld => {
  return marketplaceWorld({
    catalogue: {
      items: [catalogueModule('weather', 'Weather widgets')],
      nextCursor: null,
      verified: true,
      fetchedAt: '2026-11-09T09:00:00Z',
      unverifiedMessage: null,
    },
    installed: [installedModule('clock', 'Clock')],
  })
}

/**
 * Types the password into the open confirmation dialog and presses its submit button.
 * @param page The Playwright page under test.
 * @param dialogName The dialog's accessible name.
 * @param password What to type.
 * @returns Resolves once the button was pressed.
 */
const confirmWith = async (page: Page, dialogName: string, password: string): Promise<void> => {
  const dialog = page.getByRole('dialog', { name: dialogName })
  await dialog.getByLabel('Your current password').fill(password)
  await dialog.getByRole('button', { name: dialogName, exact: true }).click()
}

test('a fresh panel shows the switch off and makes no marketplace read but its status', async ({ page }) => {
  const recorder = await openMarketplace(page, stockedWorld(), '/')
  await openSection(page, 'Settings')
  await mainNavigation(page).getByRole('link', { name: 'Marketplace', exact: true }).click()

  await expect(page).toHaveURL(/\/marketplace$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Marketplace' })).toBeVisible()
  await expect(page.getByTestId('marketplace-switch-state')).toHaveText('Off')
  await expect(page.getByRole('button', { name: 'Turn on the marketplace' })).toBeEnabled()
  await expect(page.getByTestId('marketplace-sent-items')).toContainText('The panel version')
  await expect(page.getByRole('tablist')).toHaveCount(0)
  await page.waitForLoadState('networkidle')

  // The positive control: the recorder does see marketplace traffic, it is just only the status.
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/status/)).not.toEqual([])
  expect(recorder.matching(GATED_READ)).toEqual([])
})

test('turning the marketplace on with the password makes the catalogue reads appear', async ({ page }) => {
  const recorder = await openMarketplace(page, stockedWorld())
  expect(recorder.matching(GATED_READ)).toEqual([])

  await page.getByRole('button', { name: 'Turn on the marketplace' }).click()
  await confirmWith(page, 'Turn on the marketplace', MARKETPLACE_PASSWORD)

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByTestId('marketplace-switch-state')).toHaveText('On')
  await expect(page.getByRole('tab', { name: 'Available' })).toBeVisible()
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/catalogue/)).not.toEqual([])
  expect(recorder.matching(/^PUT \/api\/v1\/marketplace\/settings/).map((entry) => {
    return entry.body
  })).toEqual([{ enabled: true, password: MARKETPLACE_PASSWORD }])

  // The installed list is read when its tab opens, not before.
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/installed/)).toEqual([])
  await page.getByRole('tab', { name: 'Installed' }).click()
  await expect(page.getByRole('cell', { name: /^Clock/ })).toBeVisible()
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/installed/)).not.toEqual([])
})

test('a wrong password keeps the dialog open with the panel’s sentence and the switch off', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/marketplace\/settings$/)
  const recorder = await openMarketplace(page, stockedWorld())

  await page.getByRole('button', { name: 'Turn on the marketplace' }).click()
  await confirmWith(page, 'Turn on the marketplace', 'not-the-password')

  const dialog = page.getByRole('dialog', { name: 'Turn on the marketplace' })
  await expect(dialog.getByTestId('marketplace-dialog-error')).toHaveText(WRONG_PASSWORD_MESSAGE)
  await expect(page.getByTestId('marketplace-switch-state')).toHaveText('Off')
  await expect(page.getByRole('tablist')).toHaveCount(0)
  expect(recorder.matching(GATED_READ)).toEqual([])

  // The inverse control: the same dialog, the right password, and the switch moves.
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Turn on the marketplace', exact: true }).click()
  await expect(page.getByTestId('marketplace-switch-state')).toHaveText('On')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('turning the marketplace off removes the tabs and stops the reads', async ({ page }) => {
  const world = stockedWorld()
  world.status = { ...world.status, enabled: true }
  const recorder = await openMarketplace(page, world)
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
  const readsBefore = recorder.matching(GATED_READ).length
  expect(readsBefore).toBeGreaterThan(0)

  await page.getByRole('button', { name: 'Turn off the marketplace' }).click()
  await confirmWith(page, 'Turn off the marketplace', MARKETPLACE_PASSWORD)

  await expect(page.getByTestId('marketplace-switch-state')).toHaveText('Off')
  await expect(page.getByRole('tablist')).toHaveCount(0)
  await expect(page.getByTestId('module-card-weather')).toHaveCount(0)
  await page.waitForLoadState('networkidle')
  expect(recorder.matching(GATED_READ)).toHaveLength(readsBefore)
})

test('a server with no marketplace host cannot be switched on, and says why', async ({ page }) => {
  const world = stockedWorld()
  world.status = { ...world.status, hostConfigured: false, host: null }
  await openMarketplace(page, world)

  await expect(page.getByText('No marketplace host is configured on this server.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Turn on the marketplace' })).toBeDisabled()
})
