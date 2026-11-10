import { expect, test } from '../fixtures/guarded-test'
import { openApps } from '../fixtures/open-apps'
import { FINISHED_APP_TASK, appTask, appsWorld, hostApp, type AppsWorld } from '../fixtures/stub-apps-routes'
import { EMPTY_STACK, READY_STACK } from '../fixtures/stub-stack-routes'
import type { StackStatus } from '../../src/types/hostApp'
import type { Page } from '@playwright/test'

// Issue #517: the first-run wizard installs the web server (required), then any runtimes, one at a
// time with live progress, and leaves only when the panel reports a web server installed. The
// failing path is paired with the same wizard succeeding after a retry.

/** How long the installs may take: each followed task is re-read every two seconds. */
const INSTALL_TIMEOUT_MS = 20_000

/**
 * A fresh host whose installs add the app to the installed list, and whose stack status follows it.
 * @param overrides The fields to change.
 * @returns The world and the stack function that reads it.
 */
const freshHost = (overrides: Partial<AppsWorld> = {}): { world: AppsWorld; stack: () => StackStatus } => {
  const world = appsWorld(overrides)
  world.onInstall = (id): void => {
    world.installed = [...world.installed, hostApp(id, id)]
  }
  const stack = (): StackStatus => {
    return world.installed.some((app) => {
      return app.appId === 'nginx' || app.appId === 'apache'
    })
      ? READY_STACK
      : EMPTY_STACK
  }
  return { world, stack }
}

/**
 * Chooses nginx and walks to the review step with the given runtimes ticked.
 * @param page The Playwright page under test.
 * @param runtimes The names of the runtimes to tick on the second step.
 * @returns Resolves once the review is on screen.
 */
const walkToReview = async (page: Page, runtimes: string[]): Promise<void> => {
  await page.getByRole('radio', { name: 'nginx' }).check({ force: true })
  await page.getByRole('button', { name: 'Next' }).click()
  for (const runtime of runtimes) {
    await page.getByRole('checkbox', { name: runtime }).check({ force: true })
  }
  await page.getByRole('button', { name: 'Next' }).click()
  await expect(page.getByRole('button', { name: 'Install' })).toBeVisible()
}

test('the wizard will not go past the web server step until one is chosen', async ({ page }) => {
  const { world, stack } = freshHost()
  await openApps(page, world, { path: '/', stack })

  await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled()

  await page.getByRole('radio', { name: 'nginx' }).check({ force: true })
  await expect(page.getByRole('button', { name: 'Next' })).toBeEnabled()
})

test('the wizard installs the web server first, then the runtimes, and then lets the person into the panel', async ({
  page,
}) => {
  const { world, stack } = freshHost()
  const recorder = await openApps(page, world, { path: '/', stack })

  await walkToReview(page, ['PHP 8.3'])
  await expect(page.getByText('nginx', { exact: true })).toBeVisible()
  await expect(page.getByText('PHP 8.3')).toBeVisible()
  // Nothing is installed until the person confirms.
  expect(recorder.matching(/POST .*\/install$/)).toHaveLength(0)

  await page.getByRole('button', { name: 'Install' }).click()

  await expect(page.getByRole('link', { name: 'Open the panel' })).toBeVisible({ timeout: INSTALL_TIMEOUT_MS })
  const installs = recorder.matching(/POST .*\/install$/).map((request) => {
    return request.path
  })
  expect(installs).toEqual(['/api/v1/apps/nginx/install', '/api/v1/apps/php-8.3/install'])
  await expect(page.getByTestId('stack-queue-nginx').getByText('Installed')).toBeVisible()
  await expect(page.getByTestId('stack-queue-php-8.3').getByText('Installed')).toBeVisible()

  await page.getByRole('link', { name: 'Open the panel' }).click()
  await expect(page).not.toHaveURL(/\/setup\/stack$/)
})

test('runtimes are optional: choosing none installs only the web server', async ({ page }) => {
  const { world, stack } = freshHost()
  const recorder = await openApps(page, world, { path: '/', stack })

  await walkToReview(page, [])
  await expect(page.getByText('None. Add runtimes later from the app store.')).toBeVisible()
  await page.getByRole('button', { name: 'Install' }).click()

  await expect(page.getByRole('link', { name: 'Open the panel' })).toBeVisible({ timeout: INSTALL_TIMEOUT_MS })
  expect(recorder.matching(/POST .*\/install$/)).toHaveLength(1)
})

test('a failed web server install keeps the wizard open and a retry can finish it', async ({ page }) => {
  const failed = {
    ...FINISHED_APP_TASK,
    status: 'failed',
    statusName: 'Failed',
    percent: 60,
    failure: { code: 'AgentAppInstallFailed', message: 'The package manager could not install nginx.' },
  }
  const { world, stack } = freshHost({ taskScript: [appTask(), failed] })
  const installAdds = world.onInstall
  // The first attempt fails without installing anything.
  world.onInstall = null
  const recorder = await openApps(page, world, { path: '/', stack })

  await walkToReview(page, [])
  await page.getByRole('button', { name: 'Install' }).click()

  await expect(page.getByText('The package manager could not install nginx.')).toBeVisible({ timeout: INSTALL_TIMEOUT_MS })
  await expect(page.getByText('The web server could not be installed, so sites cannot be created yet. Try again.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open the panel' })).toHaveCount(0)

  // The retry succeeds: the same wizard now reaches the way into the panel.
  world.taskScript = [appTask(), FINISHED_APP_TASK]
  world.onInstall = installAdds
  await page.getByRole('button', { name: 'Try the failed ones again' }).click()

  await expect(page.getByRole('link', { name: 'Open the panel' })).toBeVisible({ timeout: INSTALL_TIMEOUT_MS })
  expect(recorder.matching(/POST .*\/install$/)).toHaveLength(2)
})
