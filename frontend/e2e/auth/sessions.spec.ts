import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import type { Session } from '../../src/types/auth'

/** The device making the request. */
const CURRENT: Session = {
  id: '00000000-0000-0000-0000-0000000000a1',
  issuedAt: '2026-08-30T09:00:00+00:00',
  expiresAt: '2026-09-13T09:00:00+00:00',
  ipAddress: '203.0.113.7',
  userAgent: 'Chrome on Linux',
  isCurrent: true,
}

/** Another device the same person signed in from. */
const OTHER: Session = {
  id: '00000000-0000-0000-0000-0000000000a2',
  issuedAt: '2026-08-29T18:30:00+00:00',
  expiresAt: '2026-09-12T18:30:00+00:00',
  ipAddress: '198.51.100.4',
  userAgent: 'Safari on iPhone',
  isCurrent: false,
}

/**
 * Fulfils `GET /api/v1/sessions` with a chosen list.
 * @param page The Playwright page whose network the route is installed on.
 * @param sessions The devices the panel reports.
 * @returns Resolves once the route is installed.
 */
const stubSessions = async (page: Page, sessions: Session[]): Promise<void> => {
  await page.route('**/api/v1/sessions', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessions) })
  })
}

test('the sessions screen lists the devices and marks the one being used', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])

  await page.goto('/settings/sessions')

  await expect(page.getByText('Chrome on Linux')).toBeVisible()
  await expect(page.getByText('Safari on iPhone')).toBeVisible()
  await expect(page.getByText('This device')).toBeVisible()
})

test('a listed session never carries a token or a hash of one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])

  await page.goto('/settings/sessions')
  await expect(page.getByText('Chrome on Linux')).toBeVisible()

  // The DTO has no field a secret could occupy; this asserts the rendered page agrees.
  await expect(page.locator('body')).not.toContainText('tokenHash')
})

test('ending another device asks first, then removes its row', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])
  await page.route(`**/api/v1/sessions/${OTHER.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.goto('/settings/sessions')
  await page
    .getByRole('row', { name: /Safari on iPhone/ })
    .getByRole('button', { name: 'Sign out' })
    .click()

  // Confirmation first: the row the user clicks may be the session they are reading from.
  await expect(page.getByText('End this session?')).toBeVisible()
  await page.getByRole('button', { name: 'End' }).click()

  await expect(page.getByText('Safari on iPhone')).toBeHidden()
  await expect(page.getByText('Chrome on Linux')).toBeVisible()
})

test('signing out everywhere returns to the sign-in screen', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])
  await page.route('**/api/v1/auth/logout-all', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })

  await page.goto('/settings/sessions')

  // Wait for the screen to be ready before making refresh fail. `goto` resolves on
  // load, while the router guard is still restoring the session in the background —
  // swapping the route any earlier makes that restore fail and bounces the visitor to
  // the sign-in screen before this test has done anything.
  const signOutEverywhere = page.getByRole('button', { name: 'Sign out everywhere' })
  await expect(signOutEverywhere).toBeVisible()

  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RefreshTokenInvalidUnauthorized', detail: 'Your session has ended.' }),
    })
  })
  await signOutEverywhere.click()

  await expect(page).toHaveURL('/login')
})
