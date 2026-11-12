import { expect, test } from '../fixtures/guarded-test'
import { openApps } from '../fixtures/open-apps'
import {
  APP_TASK_ID,
  FINISHED_APP_TASK,
  appTask,
  appsWorld,
  hostApp,
  installableApp,
  type AppsWorld,
} from '../fixtures/stub-apps-routes'
import type { Locator, Page } from '@playwright/test'

// Issue #517: the app store lists the installed apps one server page at a time, offers what the
// package sources can still install, and installs, removes and controls apps. Every refusal below is
// paired with the same action succeeding, so a handler that never ran cannot pass.

/** How long a followed task may take to finish: the store re-reads it every two seconds. */
const OPERATION_TIMEOUT_MS = 10_000

/**
 * A host with nginx and PHP 8.3 installed and PHP 8.4 and Node.js 22 still on offer.
 * @param overrides The fields to change.
 * @returns The world the stubs answer from.
 */
const stockedWorld = (overrides: Partial<AppsWorld> = {}): AppsWorld => {
  return appsWorld({
    installed: [hostApp('nginx', 'nginx'), hostApp('php-8.3', 'PHP 8.3')],
    installable: [
      installableApp('nginx', 'nginx', { installed: true }),
      installableApp('php-8.3', 'PHP 8.3', { installed: true }),
      installableApp('php-8.4', 'PHP 8.4'),
      installableApp('nodejs-22', 'Node.js 22'),
    ],
    ...overrides,
  })
}

/**
 * The table of installed apps.
 * @param page The Playwright page under test.
 * @returns Its locator.
 */
const installedTable = (page: Page): Locator => {
  return page.getByRole('table', { name: 'Apps installed on this server' })
}

/**
 * The table of apps that can still be installed.
 * @param page The Playwright page under test.
 * @returns Its locator.
 */
const availableTable = (page: Page): Locator => {
  return page.getByRole('table', { name: 'Apps this server can install' })
}

test('the app store lists what is installed and offers only what is not', async ({ page }) => {
  await openApps(page, stockedWorld())

  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toBeVisible()
  await expect(installedTable(page).getByRole('row', { name: /PHP 8\.3/ })).toBeVisible()
  await expect(availableTable(page).getByRole('row', { name: /PHP 8\.4/ })).toBeVisible()
  await expect(availableTable(page).getByRole('row', { name: /Node\.js 22/ })).toBeVisible()
  // The control: an app already installed is not offered a second time.
  await expect(availableTable(page).getByRole('row', { name: /PHP 8\.3/ })).toHaveCount(0)
})

test('the search is the panel\'s: it is sent as a query and the panel narrows the rows', async ({ page }) => {
  const recorder = await openApps(page, stockedWorld())
  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toBeVisible()

  await page.getByRole('searchbox', { name: 'Search installed apps' }).fill('php')

  await expect.poll(() => {return recorder.matching(/GET .*search=php/).length}).toBeGreaterThan(0)
  await expect(installedTable(page).getByRole('row', { name: /PHP 8\.3/ })).toBeVisible()
  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toHaveCount(0)
})

test('the filter sheet narrows by kind through the panel', async ({ page }) => {
  const recorder = await openApps(page, stockedWorld())
  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toBeVisible()

  await page.getByRole('button', { name: 'Filters' }).click()
  const sheet = page.getByRole('dialog')
  await sheet.getByRole('combobox', { name: 'Kind' }).click()
  await page.getByRole('option', { name: 'Runtime' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect.poll(() => {return recorder.matching(/GET .*kind=runtime/).length}).toBeGreaterThan(0)
  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toHaveCount(0)
  await expect(installedTable(page).getByRole('row', { name: /PHP 8\.3/ })).toBeVisible()
})

test('on a phone the filters open as a sheet that fits the screen', async ({ page }) => {
  await openApps(page, stockedWorld())
  await page.setViewportSize({ width: 390, height: 844 })

  await page.getByRole('button', { name: 'Filters' }).click()

  const box = await page.getByRole('dialog').boundingBox()
  expect(box).not.toBeNull()
  expect((box?.x ?? -1) >= 0 && (box?.x ?? 0) + (box?.width ?? 0) <= 390).toBe(true)
})

test('installing an app starts a task, shows its progress and lists the app when it ends', async ({ page }) => {
  const world = stockedWorld({ taskScript: [appTask({ subject: 'php-8.4' }), { ...FINISHED_APP_TASK, subject: 'php-8.4' }] })
  world.onInstall = (id): void => {
    world.installed = [...world.installed, hostApp(id, 'PHP 8.4')]
    world.installable = world.installable.map((app) => {
      return app.appId === id ? { ...app, installed: true } : app
    })
  }
  const recorder = await openApps(page, world)
  await expect(availableTable(page).getByRole('row', { name: /PHP 8\.4/ })).toBeVisible()

  await page.getByRole('button', { name: 'Install PHP 8.4' }).click()

  await expect.poll(() => {return recorder.matching(/POST \/api\/v1\/apps\/php-8\.4\/install$/).length}).toBe(1)
  await expect(page.getByTestId('apps-operation-php-8.4')).toBeVisible()
  await expect(installedTable(page).getByRole('row', { name: /PHP 8\.4/ })).toBeVisible({ timeout: OPERATION_TIMEOUT_MS })
  await expect(availableTable(page).getByRole('row', { name: /PHP 8\.4/ })).toHaveCount(0)
})

test('stopping an app\'s service asks the panel and shows the new state', async ({ page }) => {
  const recorder = await openApps(page, stockedWorld())
  const row = installedTable(page).getByRole('row', { name: /nginx/ })
  await expect(row.getByText('Running')).toBeVisible()

  await page.getByRole('button', { name: 'Actions for nginx' }).click()
  await page.getByRole('menuitem', { name: 'Stop' }).click()

  await expect.poll(() => {return recorder.matching(/POST \/api\/v1\/apps\/nginx\/service\/stop$/).length}).toBe(1)
  await expect(row.getByText('Stopped')).toBeVisible()

  // The inverse: a stopped service offers Start and no longer offers Stop.
  await page.getByRole('button', { name: 'Actions for nginx' }).click()
  await expect(page.getByRole('menuitem', { name: 'Start', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Stop' })).toHaveCount(0)
})

test('removing an app that sites use is refused and lists those sites', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/apps\/php-8\.3\/uninstall$/)
  const world = stockedWorld({ inUse: { 'php-8.3': ['shop.example.com', 'blog.example.com'] } })
  const recorder = await openApps(page, world)

  await page.getByRole('button', { name: 'Actions for PHP 8.3' }).click()
  await page.getByRole('menuitem', { name: 'Uninstall' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Uninstall' }).click()

  const refusal = page.getByTestId('apps-in-use')
  await expect(refusal).toBeVisible()
  await expect(refusal.getByText('shop.example.com')).toBeVisible()
  await expect(refusal.getByText('blog.example.com')).toBeVisible()
  // The refusal started no operation.
  await expect(page.getByTestId('apps-operation-php-8.3')).toHaveCount(0)
  expect(recorder.matching(/POST \/api\/v1\/apps\/php-8\.3\/uninstall$/)).toHaveLength(1)
})

test('removing an app no site uses starts the removal', async ({ page }) => {
  const world = stockedWorld()
  world.onUninstall = (id): void => {
    world.installed = world.installed.filter((app) => {
      return app.appId !== id
    })
  }
  const recorder = await openApps(page, world)

  await page.getByRole('button', { name: 'Actions for PHP 8.3' }).click()
  await page.getByRole('menuitem', { name: 'Uninstall' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Uninstall' }).click()

  await expect.poll(() => {return recorder.matching(/POST \/api\/v1\/apps\/php-8\.3\/uninstall$/).length}).toBe(1)
  await expect(page.getByTestId('apps-in-use')).toHaveCount(0)
  await expect(installedTable(page).getByRole('row', { name: /PHP 8\.3/ })).toHaveCount(0, { timeout: OPERATION_TIMEOUT_MS })
})

// Issue #524: the panel clears a row's task id when its operation settles, and the page follows only
// rows that are installing or being removed. A row that still carried a finished task (an answer from
// before the fix) must not be followed, because following it ends at once, shrinks the operations and
// makes the page reload the table, which answers the same task again, without end.

test('an app whose operation settled is not followed, so the idle page reads no task', async ({ page }) => {
  const world = appsWorld({
    installed: [hostApp('nginx', 'nginx', { taskId: APP_TASK_ID })],
    installable: [installableApp('nginx', 'nginx', { installed: true })],
  })
  const recorder = await openApps(page, world)

  await expect(installedTable(page).getByRole('row', { name: /nginx/ })).toBeVisible()
  await page.waitForLoadState('networkidle')

  expect(recorder.matching(/GET \/api\/v1\/tasks\//)).toHaveLength(0)
  await expect(page.getByTestId('apps-operation-nginx')).toHaveCount(0)
})

test('the control: an app that is still installing is followed through its task', async ({ page }) => {
  const world = appsWorld({
    installed: [hostApp('php-8.4', 'PHP 8.4', { state: 'installing', stateName: 'Installing', taskId: APP_TASK_ID })],
    installable: [installableApp('php-8.4', 'PHP 8.4')],
  })
  const recorder = await openApps(page, world)

  await expect.poll(() => {return recorder.matching(/GET \/api\/v1\/tasks\//).length}).toBeGreaterThan(0)
  await expect(page.getByTestId('apps-operation-php-8.4')).toBeVisible()
})

// A row left installing by a panel that died names a task that already finished. Following it would
// end at once, reload the table and meet the same row again, without end; it is read once, then left.

test('a row stuck installing whose task already finished is read once and not followed again', async ({ page }) => {
  const world = appsWorld({
    installed: [hostApp('php-8.4', 'PHP 8.4', { state: 'installing', stateName: 'Installing', taskId: APP_TASK_ID })],
    installable: [installableApp('php-8.4', 'PHP 8.4')],
    taskScript: [FINISHED_APP_TASK],
  })
  const recorder = await openApps(page, world)

  await expect.poll(() => {return recorder.matching(/GET \/api\/v1\/tasks\//).length}).toBe(1)
  await page.waitForLoadState('networkidle')

  expect(recorder.matching(/GET \/api\/v1\/tasks\//)).toHaveLength(1)
})

// Issue #532: a host whose package sources carry no Node.js module streams, or whose listing could not
// be read, says so where Node.js would have been instead of leaving it out silently. Each reason is
// paired with the same screen on a host that offers Node.js, which shows no notice.

const NO_STREAMS_SENTENCE = 'Node.js is not offered on this server: its package sources carry no Node.js module streams.'
const LISTING_FAILED_SENTENCE = 'Node.js could not be listed: the package manager could not be asked.'

test('a host without Node.js module streams says why Node.js is not offered', async ({ page }) => {
  await openApps(
    page,
    stockedWorld({
      installable: [installableApp('nginx', 'nginx', { installed: true }), installableApp('php-8.4', 'PHP 8.4')],
      nodejsUnavailable: { reason: 'noModuleStreams', message: NO_STREAMS_SENTENCE },
    }),
  )

  await expect(page.getByText(NO_STREAMS_SENTENCE)).toBeVisible()
  // The other apps are listed as usual.
  await expect(availableTable(page).getByRole('row', { name: /PHP 8\.4/ })).toBeVisible()
  await expect(availableTable(page).getByRole('row', { name: /Node\.js/ })).toHaveCount(0)
})

test('a listing that failed says so, apart from a host that carries no Node.js', async ({ page }) => {
  await openApps(
    page,
    stockedWorld({
      installable: [installableApp('php-8.4', 'PHP 8.4')],
      nodejsUnavailable: { reason: 'listingFailed', message: LISTING_FAILED_SENTENCE },
    }),
  )

  await expect(page.getByText(LISTING_FAILED_SENTENCE)).toBeVisible()
  await expect(page.getByText(NO_STREAMS_SENTENCE)).toHaveCount(0)
})

test('the control: a host that offers Node.js shows no notice about it', async ({ page }) => {
  await openApps(page, stockedWorld())

  await expect(availableTable(page).getByRole('row', { name: /Node\.js 22/ })).toBeVisible()
  await expect(page.getByText(/Node\.js is not offered|Node\.js could not be listed/)).toHaveCount(0)
})
