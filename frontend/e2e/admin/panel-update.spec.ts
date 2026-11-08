import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelUpdateState } from '../../src/types/panelUpdate'

// The "Panel updates" screen (issue #382): the version facts, the administrator-only action, the
// request that carries no body, and a refusal shown in the panel's own words.

const PANEL_UPDATE_PATTERN = '**/api/v1/panel-update'

const NEWER: PanelUpdateState = {
  currentVersion: '1.0.0',
  channel: 'stable',
  available: { version: '1.1.0', releasedAt: '2026-11-05T00:00:00Z', notesUrl: 'https://example.net/notes/1.1.0' },
  lastCheckedAt: '2026-11-06T08:00:00Z',
  previousVersion: '0.9.0',
  updateInProgress: false,
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
      await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ taskId: 't-1' }) })
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

test('a refused request shows the panel’s own sentence', async ({ page }) => {
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
