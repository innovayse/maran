import { expect, test } from '../fixtures/guarded-test'
import { openMarketplace } from '../fixtures/open-marketplace'
import {
  MARKETPLACE_PASSWORD,
  WRONG_PASSWORD_MESSAGE,
  catalogueModule,
  detailOf,
  enabledWorld,
  installedModule,
  operationTask,
  type MarketplaceWorld,
} from '../fixtures/stub-marketplace-routes'
import type { ModuleUpdate } from '../../src/types/marketplace'
import type { Page } from '@playwright/test'

// Issue #187: the update check is the administrator's act (the Updates tab stays silent until its
// button is pressed), an update asks for the same acknowledgement and password as an install, and
// removing a module keeps its data and says so. Each negative claim is paired with the action that
// must make the opposite true.

/** The module that has a newer version. */
const CLOCK_UPDATE: ModuleUpdate = {
  id: 'clock',
  displayName: 'Clock',
  installedVersion: '2.0.0',
  availableVersion: '2.1.0',
  revoked: false,
  revokedMessage: null,
  canUpdate: true,
  cannotUpdateMessage: null,
}

/** A module whose newer version needs a newer panel, so it is listed with the reason and no button. */
const NOTES_UPDATE: ModuleUpdate = {
  id: 'notes',
  displayName: 'Notes',
  installedVersion: '1.0.0',
  availableVersion: '3.0.0',
  revoked: false,
  revokedMessage: null,
  canUpdate: false,
  cannotUpdateMessage: 'Needs panel version 2.0.0 or newer.',
}

/**
 * A world with two installed modules and an update check that finds two newer versions.
 * @returns The world, with the marketplace on.
 */
const updatableWorld = (): MarketplaceWorld => {
  const clock = catalogueModule('clock', 'Clock', {
    state: 'updateAvailable',
    installedVersion: '2.0.0',
    latestVersion: '2.1.0',
  })
  const world = enabledWorld({
    installed: [installedModule('clock', 'Clock'), installedModule('notes', 'Notes', { version: '1.0.0' })],
    updates: {
      items: [CLOCK_UPDATE, NOTES_UPDATE],
      verified: true,
      checkedAt: '2026-11-09T09:05:00Z',
      unverifiedMessage: null,
    },
    details: { clock: detailOf(clock) },
    taskScript: [operationTask({ kind: 'ModuleUpdate', subject: 'clock' })],
  })
  world.catalogue = { ...world.catalogue, items: [clock] }
  return world
}

/**
 * A world with one removable installed module whose removal task is already finished.
 * @returns The world; accepting a removal leaves the module in the removed state with its data kept.
 */
const removableWorld = (): MarketplaceWorld => {
  const world = enabledWorld({
    installed: [installedModule('clock', 'Clock', { canRollback: true, previousVersion: '1.9.0' })],
    taskScript: [
      operationTask({
        kind: 'ModuleRemove',
        subject: 'clock',
        status: 'completed',
        statusName: 'Completed',
        percent: 100,
        finishedAt: '2026-11-09T10:00:05Z',
        log: 'Checking entitlement\nDone - restart required',
      }),
    ],
  })
  world.onOperation = (name, id): void => {
    if (name === 'remove') {
      world.installed = [
        installedModule(id, 'Clock', { lifecycle: 'removed', dataKept: true, canRemove: false, canRollback: false }),
      ]
    }
  }
  return world
}

/**
 * Opens the Installed tab.
 * @param page The Playwright page under test.
 * @returns Resolves once the tab is selected.
 */
const openInstalledTab = async (page: Page): Promise<void> => {
  await page.getByRole('tab', { name: 'Installed' }).click()
  await expect(page.getByRole('cell', { name: /^Clock/ })).toBeVisible()
}

test('the Updates tab stays silent until Check for updates is pressed', async ({ page }) => {
  const recorder = await openMarketplace(page, updatableWorld())

  await page.getByRole('tab', { name: 'Installed' }).click()
  await expect(page.getByRole('cell', { name: /^Clock/ })).toBeVisible()
  await page.getByRole('tab', { name: 'Updates' }).click()
  await expect(page.getByText('Updates are checked only when you press Check for updates.')).toBeVisible()
  await page.waitForLoadState('networkidle')
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/updates/)).toEqual([])

  // The positive control: pressing the button is what makes the one request.
  await page.getByRole('button', { name: 'Check for updates' }).click()
  const table = page.getByRole('table', { name: 'Available module updates' })
  await expect(table.getByRole('cell', { name: 'Clock', exact: true })).toBeVisible()
  await expect(table.getByRole('cell', { name: '2.1.0', exact: true })).toBeVisible()
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/updates/)).toHaveLength(1)

  await expect(page.getByRole('button', { name: 'Update Clock' })).toBeEnabled()
  await expect(table).toContainText('Needs panel version 2.0.0 or newer.')
  await expect(page.getByRole('button', { name: 'Update Notes' })).toHaveCount(0)
})

test('an update asks for the acknowledgement and the password, then follows the task', async ({ page }) => {
  const recorder = await openMarketplace(page, updatableWorld())
  await page.getByRole('tab', { name: 'Updates' }).click()
  await page.getByRole('button', { name: 'Check for updates' }).click()
  await page.getByRole('button', { name: 'Update Clock' }).click()

  const dialog = page.getByRole('dialog', { name: 'Update Clock' })
  await expect(dialog.getByTestId('module-manifest')).toBeVisible()
  const submit = dialog.getByRole('button', { name: 'Update', exact: true })
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await expect(submit).toBeDisabled()
  await dialog
    .getByRole('checkbox', { name: /^I understand that the new version runs inside the panel/ })
    .check({ force: true })
  await expect(submit).toBeEnabled()
  await submit.click()

  await expect(page.getByTestId('module-operation-clock')).toContainText('Updating Clock')
  expect(recorder.matching(/^POST \/api\/v1\/marketplace\/modules\/clock\/update$/).map((entry) => {
    return entry.body
  })).toEqual([{ version: '2.1.0', acknowledged: true, password: MARKETPLACE_PASSWORD }])
})

test('an update check that could not be verified lists the modules but offers no update', async ({ page }) => {
  const world = updatableWorld()
  world.updates = { ...world.updates, verified: false, unverifiedMessage: 'The update list signature could not be checked.' }
  await openMarketplace(page, world)
  await page.getByRole('tab', { name: 'Updates' }).click()
  await page.getByRole('button', { name: 'Check for updates' }).click()

  await expect(page.getByTestId('updates-unverified')).toContainText('The update list signature could not be checked.')
  await expect(page.getByRole('button', { name: 'Update Clock' })).toBeDisabled()
})

test('removing a module keeps its data, says so and needs the password', async ({ page }) => {
  const recorder = await openMarketplace(page, removableWorld())
  await openInstalledTab(page)
  await expect(page.getByTestId('module-data-kept')).toHaveCount(0)

  await page.getByRole('button', { name: 'Remove Clock' }).click()
  const dialog = page.getByRole('dialog', { name: 'Remove Clock' })
  await expect(dialog).toContainText('Its data is kept')
  const submit = dialog.getByRole('button', { name: 'Remove module', exact: true })
  await expect(submit).toBeDisabled()

  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await expect(submit).toBeEnabled()
  await submit.click()

  expect(recorder.matching(/^POST \/api\/v1\/marketplace\/modules\/clock\/remove$/).map((entry) => {
    return entry.body
  })).toEqual([{ password: MARKETPLACE_PASSWORD }])
  await expect(page.getByTestId('module-data-kept')).toHaveText('The module is removed and its data is kept.', {
    timeout: 10_000,
  })
  await expect(page.getByRole('button', { name: 'Remove Clock' })).toHaveCount(0)
})

test('a wrong password on a removal keeps the dialog open and removes nothing', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/marketplace\/modules\/clock\/remove$/)
  await openMarketplace(page, removableWorld())
  await openInstalledTab(page)

  await page.getByRole('button', { name: 'Remove Clock' }).click()
  const dialog = page.getByRole('dialog', { name: 'Remove Clock' })
  await dialog.getByLabel('Your current password').fill('not-the-password')
  await dialog.getByRole('button', { name: 'Remove module', exact: true }).click()

  await expect(dialog.getByTestId('marketplace-dialog-error')).toHaveText(WRONG_PASSWORD_MESSAGE)
  await expect(page.getByTestId('module-operation-clock')).toHaveCount(0)
  await expect(page.getByTestId('module-data-kept')).toHaveCount(0)

  // The inverse control: the right password in the same dialog removes it.
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Remove module', exact: true }).click()
  await expect(page.getByTestId('module-data-kept')).toBeVisible({ timeout: 10_000 })
})

test('a rollback needs the password too and starts a task for the module', async ({ page }) => {
  const world = removableWorld()
  world.taskScript = [operationTask({ kind: 'ModuleRollback', subject: 'clock' })]
  const recorder = await openMarketplace(page, world)
  await openInstalledTab(page)

  await expect(page.getByText('Previous version: 1.9.0')).toBeVisible()
  await page.getByRole('button', { name: 'Roll back Clock' }).click()
  const dialog = page.getByRole('dialog', { name: 'Roll back Clock' })
  const submit = dialog.getByRole('button', { name: 'Roll back module', exact: true })
  await expect(submit).toBeDisabled()
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await submit.click()

  await expect(page.getByTestId('module-operation-clock')).toContainText('Rolling back Clock')
  expect(recorder.matching(/^POST \/api\/v1\/marketplace\/modules\/clock\/rollback$/).map((entry) => {
    return entry.body
  })).toEqual([{ password: MARKETPLACE_PASSWORD }])
})
