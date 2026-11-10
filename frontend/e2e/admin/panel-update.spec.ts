import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelUpdateState } from '../../src/types/panelUpdate'
import type { Page } from '@playwright/test'

// The "Panel updates" screen (issue #382): the version facts, the administrator-only action, the
// request that carries no body, and a refusal shown in the panel's own words.

const PANEL_UPDATE_PATTERN = '**/api/v1/panel-update'
const PANEL_UPDATE_CHECK_PATTERN = '**/api/v1/panel-update/check'

const NEWER: PanelUpdateState = {
  currentVersion: '1.0.0',
  channel: 'stable',
  available: { version: '1.1.0', releasedAt: '2026-11-05T00:00:00Z', notesUrl: 'https://example.net/notes/1.1.0' },
  lastCheckedAt: '2026-11-06T08:00:00Z',
  previousVersion: '0.9.0',
  updateInProgress: false,
  checkInProgress: false,
  canStart: true,
}

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

const answerWith = async (page: Page, state: PanelUpdateState): Promise<void> => {
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })
}

test('an administrator sees the versions and requests the update with an empty body', async ({ page }) => {
  await shell(page)
  let postBody: string | null = null
  let reads = 0
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      postBody = route.request().postData()
      await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ taskId: '00000000-0000-0000-0000-0000000000a1' }) })
      return
    }
    reads += 1
    const state = reads > 1 ? { ...NEWER, updateInProgress: true } : NEWER
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('1.1.0')).toBeVisible()
  await expect(page.getByText('0.9.0')).toBeVisible()
  await expect(page.getByRole('link', { name: /Read the release notes/ })).toHaveAttribute(
    'href',
    'https://example.net/notes/1.1.0',
  )

  await page.getByRole('button', { name: 'Update now' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Update now' }).click()

  await expect(page.getByText('The update was requested.', { exact: false })).toBeVisible()
  await expect(page.getByText('An update is queued or running.')).toBeVisible()
  expect(postBody).toBeNull()
})

test('a caller who may not start an update is offered no button', async ({ page }) => {
  await shell(page)
  await answerWith(page, { ...NEWER, canStart: false })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('1.1.0')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Update now' })).toHaveCount(0)
})

test('a hostile release-notes address is never linked', async ({ page }) => {
  await shell(page)
  await answerWith(page, { ...NEWER, available: { version: '1.1.0', releasedAt: null, notesUrl: 'javascript:alert(1)' } })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('No release notes published')).toBeVisible()
  await expect(page.getByRole('link', { name: /Read the release notes/ })).toHaveCount(0)
})

test('a refused request shows the panel’s own sentence', async ({ page, guard }) => {
  // The refusal is the subject: an update is already running, so the request is a 409.
  guard.allow(409, /\/api\/v1\/panel-update$/)
  await shell(page)
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'PanelUpdateRunning', title: 'Refused', detail: 'An update is already running.' }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(NEWER) })
  })

  await page.goto('/settings/panel-update')
  await page.getByRole('button', { name: 'Update now' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Update now' }).click()

  await expect(page.getByText('An update is already running.')).toBeVisible()
})

test('a panel that was never checked says so instead of claiming it is up to date (#506)', async ({ page }) => {
  await shell(page)
  await answerWith(page, { ...NEWER, available: null, lastCheckedAt: null, previousVersion: null })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('Not checked yet')).toHaveCount(2)
  await expect(page.getByText('None, the panel is up to date')).toHaveCount(0)
  await expect(page.getByTestId('panel-update-blocked')).toContainText('Nothing has been checked yet')
  await expect(page.getByRole('button', { name: 'Update now' })).toBeDisabled()
})

test('a panel whose check found nothing newer says it is up to date, with the action off (#506 control)', async ({
  page,
}) => {
  await shell(page)
  await answerWith(page, { ...NEWER, available: null })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('None, the panel is up to date')).toBeVisible()
  await expect(page.getByTestId('panel-update-blocked')).toContainText('no newer release')
  await expect(page.getByRole('button', { name: 'Update now' })).toBeDisabled()
})

test('a panel with a release to install offers the action and no blocked reason (#506 control)', async ({ page }) => {
  await shell(page)
  await answerWith(page, NEWER)

  await page.goto('/settings/panel-update')

  await expect(page.getByRole('button', { name: 'Update now' })).toBeEnabled()
  await expect(page.getByTestId('panel-update-blocked')).toHaveCount(0)
})

// "Check for updates" (issue #516): an administrator-only request with no body, followed while the
// server reports it in progress, and judged by whether the last-checked time moved.

const OLD_CHECK = '2026-11-06T08:00:00Z'
const NEW_CHECK = '2026-11-09T09:30:00Z'

test('a check is requested with no body, shown as running, and ends with the new result (#516)', async ({ page }) => {
  await shell(page)
  let checkBody: string | null = null
  let checkRequests = 0
  let reads = 0
  await page.route(PANEL_UPDATE_CHECK_PATTERN, async (route) => {
    checkRequests += 1
    checkBody = route.request().postData()
    await route.fulfill({ status: 202 })
  })
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    reads += 1
    // Read 1 is the page opening, read 2 the one right after the request (the check is queued), read 3
    // the first tick (still running), read 4 the second tick (finished, and it found 1.2.0).
    const state: PanelUpdateState =
      reads < 2
        ? { ...NEWER, available: null }
        : reads < 4
          ? { ...NEWER, available: null, checkInProgress: true }
          : { ...NEWER, available: { version: '1.2.0', releasedAt: null, notesUrl: null }, lastCheckedAt: NEW_CHECK }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })

  await page.goto('/settings/panel-update')
  await page.getByRole('button', { name: 'Check for updates' }).click()

  await expect(page.getByTestId('panel-update-checking')).toContainText('Checking for updates')
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Update now' })).toBeDisabled()

  await expect(page.getByText('The check finished.', { exact: false })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('1.2.0')).toBeVisible()
  await expect(page.getByTestId('panel-update-checking')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeEnabled()
  expect(checkRequests).toBe(1)
  expect(checkBody).toBeNull()
})

test('a check that recorded nothing says so and offers the button again (#516)', async ({ page }) => {
  await shell(page)
  let reads = 0
  await page.route(PANEL_UPDATE_CHECK_PATTERN, async (route) => {
    await route.fulfill({ status: 202 })
  })
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    reads += 1
    const state: PanelUpdateState = { ...NEWER, lastCheckedAt: OLD_CHECK, checkInProgress: reads === 2 }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })

  await page.goto('/settings/panel-update')
  await page.getByRole('button', { name: 'Check for updates' }).click()

  await expect(page.getByText('The check did not record a result.', { exact: false })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('The check finished.', { exact: false })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeEnabled()
})

test('a check already running when the page opens is followed and the button is off (#516)', async ({ page }) => {
  await shell(page)
  let reads = 0
  await page.route(PANEL_UPDATE_PATTERN, async (route) => {
    reads += 1
    const state: PanelUpdateState =
      reads < 3 ? { ...NEWER, checkInProgress: true } : { ...NEWER, lastCheckedAt: NEW_CHECK }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })

  await page.goto('/settings/panel-update')

  await expect(page.getByTestId('panel-update-checking')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeDisabled()
  await expect(page.getByText('The check finished.', { exact: false })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeEnabled()
})

test('a refused check shows the panel’s own sentence and leaves the button on (#516)', async ({ page, guard }) => {
  // The refusal is the subject: the check is rate-limited with a 429.
  guard.allow(429, /\/api\/v1\/panel-update\/check$/)
  await shell(page)
  await answerWith(page, NEWER)
  await page.route(PANEL_UPDATE_CHECK_PATTERN, async (route) => {
    await route.fulfill({
      status: 429,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RateLimited', title: 'Too many requests', detail: 'Too many checks. Try again later.' }),
    })
  })

  await page.goto('/settings/panel-update')
  await page.getByRole('button', { name: 'Check for updates' }).click()

  await expect(page.getByText('Too many checks. Try again later.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeEnabled()
})

test('a server that cannot take a request is offered no check button, and one that can is (#516)', async ({ page }) => {
  await shell(page)
  await answerWith(page, { ...NEWER, canStart: false })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('1.1.0')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Check for updates' })).toHaveCount(0)
})

test('the check is off while an update is in progress (#516)', async ({ page }) => {
  await shell(page)
  await answerWith(page, { ...NEWER, updateInProgress: true })

  await page.goto('/settings/panel-update')

  await expect(page.getByText('An update is queued or running.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Check for updates' })).toBeDisabled()
})
