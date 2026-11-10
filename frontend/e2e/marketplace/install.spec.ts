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
import type { Locator, Page } from '@playwright/test'

// Issue #187: installing a module is the most privileged act the panel UI offers, so the dialog
// shows the SERVER's copy of the manifest before it asks, will not submit without the
// acknowledgement and the password, follows the task to its end, and finishes in "restart required"
// rather than claiming the module runs. Each refusal below is paired with the same action succeeding.

/** The module installed throughout. */
const WEATHER = catalogueModule('weather', 'Weather widgets')

/** The finished task: the panel's step lines end in "Done - restart required". */
const FINISHED = operationTask({
  status: 'completed',
  statusName: 'Completed',
  percent: 100,
  finishedAt: '2026-11-09T10:00:05Z',
  log: 'Checking entitlement\nDownloading\nVerifying signature\nStaging\nDone - restart required',
})

/**
 * A world whose install, once accepted, leaves the module staged for the next restart.
 * @returns The world, with the task running at first and finished on its second read.
 */
const installableWorld = (): MarketplaceWorld => {
  const world = enabledWorld({
    details: { weather: detailOf(WEATHER) },
    taskScript: [operationTask(), FINISHED],
  })
  world.catalogue = { ...world.catalogue, items: [WEATHER] }
  world.onOperation = (_name, id): void => {
    world.installed = [
      installedModule(id, 'Weather widgets', { version: '1.2.0', lifecycle: 'installed', restartRequired: true }),
    ]
    world.status = { ...world.status, restartRequiredModules: [id] }
    world.catalogue = {
      ...world.catalogue,
      items: [{ ...WEATHER, state: 'installed', installedVersion: '1.2.0', restartRequired: true }],
    }
  }
  return world
}

/**
 * The install button of the weather card.
 * @param page The Playwright page under test.
 * @returns Its locator.
 */
const installButton = (page: Page): Locator => {
  return page.getByRole('button', { name: 'Install Weather widgets' })
}

/**
 * Opens the install dialog and waits until the server's manifest is in it.
 * @param page The Playwright page under test.
 * @returns The dialog.
 */
const openInstallDialog = async (page: Page): Promise<Locator> => {
  await installButton(page).click()
  const dialog = page.getByRole('dialog', { name: 'Install Weather widgets' })
  await expect(dialog.getByTestId('module-manifest')).toBeVisible()
  return dialog
}

/**
 * The acknowledgement checkbox of the install dialog.
 * @param dialog The install dialog.
 * @returns Its locator.
 */
const acknowledgement = (dialog: Locator): Locator => {
  return dialog.getByRole('checkbox', { name: /^I understand that this module runs inside the panel/ })
}

test('the install dialog shows the server’s manifest and submits only with the acknowledgement and the password', async ({ page }) => {
  await openMarketplace(page, installableWorld())

  const dialog = await openInstallDialog(page)
  await expect(dialog).toContainText('Read files inside an account')
  await expect(dialog).toContainText('See the weather widgets')
  await expect(dialog).toContainText('1.2.0')

  const submit = dialog.getByRole('button', { name: 'Install', exact: true })
  await expect(submit).toBeDisabled()

  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await expect(submit).toBeDisabled()

  await acknowledgement(dialog).check({ force: true })
  await expect(submit).toBeEnabled()
})

test('an install shows its progress and ends in restart required, then the restart brings the module up', async ({ page }) => {
  const world = installableWorld()
  const recorder = await openMarketplace(page, world)

  const dialog = await openInstallDialog(page)
  await acknowledgement(dialog).check({ force: true })
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Install', exact: true }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(recorder.matching(/^POST \/api\/v1\/marketplace\/modules\/weather\/install$/).map((entry) => {
    return entry.body
  })).toEqual([{ version: '1.2.0', acknowledged: true, password: MARKETPLACE_PASSWORD }])

  const operation = page.getByTestId('module-operation-weather')
  await expect(operation).toContainText('Installing Weather widgets')
  await expect(operation.getByRole('progressbar', { name: 'Progress of the operation on Weather widgets' })).toHaveAttribute(
    'aria-valuenow',
    '40',
  )
  await expect(operation.getByText('Downloading', { exact: true })).toBeVisible()
  await expect(operation.getByText('The operation finished.')).toHaveCount(0)

  // The task is followed by reading it again: the second read is the finished one.
  await expect(operation.getByText('The operation finished.')).toBeVisible({ timeout: 10_000 })
  await expect(operation.getByRole('progressbar', { name: 'Progress of the operation on Weather widgets' })).toHaveAttribute(
    'aria-valuenow',
    '100',
  )
  await expect(operation.getByText('Done - restart required', { exact: true })).toBeVisible()
  expect(recorder.taskReads()).toBeGreaterThanOrEqual(2)

  // The module is staged, not running: the card says so and offers no second install.
  const card = page.getByTestId('module-card-weather')
  await expect(card.getByText('Restart required', { exact: true })).toBeVisible()
  await expect(card.getByRole('button', { name: /^Install / })).toHaveCount(0)

  await page.getByRole('tab', { name: 'Installed' }).click()
  const notice = page.getByTestId('restart-required')
  await expect(notice).toContainText('Weather widgets is installed and starts after the panel restarts.')
  await expect(page.getByTestId('restart-ready')).toHaveCount(0)

  await notice.getByRole('button', { name: 'Restart the panel' }).click()
  await page.getByRole('button', { name: 'Restart now' }).click()
  await expect(page.getByTestId('restart-in-progress')).toBeVisible()
  expect(recorder.matching(/^POST \/api\/v1\/marketplace\/modules\/weather\/restart$/)).toHaveLength(1)

  // The panel comes back with the module running: the page notices on its next local read.
  world.installed = [installedModule('weather', 'Weather widgets', { version: '1.2.0' })]
  world.status = { ...world.status, restartRequiredModules: [] }
  await expect(page.getByTestId('restart-ready')).toContainText('Weather widgets is ready.', { timeout: 15_000 })
  await expect(page.getByTestId('restart-required')).toHaveCount(0)
  await expect(page.getByTestId('restart-in-progress')).toHaveCount(0)
})

test('a wrong password keeps the dialog open with the panel’s sentence and starts no task', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/marketplace\/modules\/weather\/install$/)
  const recorder = await openMarketplace(page, installableWorld())

  const dialog = await openInstallDialog(page)
  await acknowledgement(dialog).check({ force: true })
  await dialog.getByLabel('Your current password').fill('not-the-password')
  await dialog.getByRole('button', { name: 'Install', exact: true }).click()

  await expect(dialog.getByTestId('marketplace-dialog-error')).toHaveText(WRONG_PASSWORD_MESSAGE)
  await expect(page.getByTestId('module-operation-weather')).toHaveCount(0)
  expect(recorder.taskReads()).toBe(0)

  // The inverse control: the same dialog accepts the right password.
  await acknowledgement(dialog).check({ force: true })
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Install', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByTestId('module-operation-weather')).toBeVisible()
})

test('a refusal from the panel is printed as received and the dialog stays', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/marketplace\/modules\/weather\/install$/)
  const world = installableWorld()
  world.operationRefusal = {
    status: 409,
    code: 'ModuleOperationInProgress',
    detail: 'Another operation on this module is already running.',
  }
  await openMarketplace(page, world)

  const dialog = await openInstallDialog(page)
  await acknowledgement(dialog).check({ force: true })
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Install', exact: true }).click()

  await expect(dialog.getByTestId('marketplace-dialog-error')).toHaveText(
    'Another operation on this module is already running.',
  )
  await expect(page.getByTestId('module-operation-weather')).toHaveCount(0)
})

test('a failed task shows the panel’s message and code and offers to try again', async ({ page }) => {
  const world = installableWorld()
  world.taskScript = [
    operationTask({
      status: 'failed',
      statusName: 'Failed',
      percent: 60,
      finishedAt: '2026-11-09T10:00:03Z',
      failure: { code: 'DownloadDigestMismatch', message: 'The downloaded package did not match its announced digest.' },
    }),
  ]
  await openMarketplace(page, world)

  const dialog = await openInstallDialog(page)
  await acknowledgement(dialog).check({ force: true })
  await dialog.getByLabel('Your current password').fill(MARKETPLACE_PASSWORD)
  await dialog.getByRole('button', { name: 'Install', exact: true }).click()

  const failure = page.getByTestId('module-operation-failure')
  await expect(failure).toContainText('The downloaded package did not match its announced digest.')
  await expect(failure).toContainText('Code: DownloadDigestMismatch')
  await expect(failure).toContainText('Modules that were running keep running.')
  await page.getByTestId('module-operation-weather').getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByRole('dialog', { name: 'Install Weather widgets' })).toBeVisible()
})

test('the install dialog works from the keyboard, traps focus, closes on Escape and returns focus', async ({ page }) => {
  const recorder = await openMarketplace(page, installableWorld())
  await expect(installButton(page)).toBeVisible()

  // Escape: open from the keyboard, close with Escape, focus is back on the button that opened it.
  await installButton(page).focus()
  await page.keyboard.press('Enter')
  const dialog = page.getByRole('dialog', { name: 'Install Weather widgets' })
  await expect(dialog.getByTestId('module-manifest')).toBeVisible()
  for (let press = 0; press < 8; press += 1) {
    await page.keyboard.press('Tab')
    await expect(dialog.locator(':focus')).toHaveCount(1)
  }
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(installButton(page)).toBeFocused()
  expect(recorder.matching(/\/install$/)).toEqual([])

  // The positive control: the same dialog completed with the keyboard alone sends the install.
  await page.keyboard.press('Enter')
  await expect(dialog.getByTestId('module-manifest')).toBeVisible()
  await acknowledgement(dialog).focus()
  await page.keyboard.press('Space')
  await expect(acknowledgement(dialog)).toBeChecked()
  await dialog.getByLabel('Your current password').focus()
  await page.keyboard.type(MARKETPLACE_PASSWORD)
  await page.keyboard.press('Enter')

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(recorder.matching(/^POST .*\/weather\/install$/)).toHaveLength(1)
})
