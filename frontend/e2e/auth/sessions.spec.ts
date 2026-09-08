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
  // The panel is stubbed as a SERVER THAT LEAKS: it answers with the session fields the SPA knows
  // about plus a `tokenHash` the DTO has no room for. That is what makes this assertion able to
  // fail at all. Stubbing the clean shape and then checking the page for `tokenHash` — which is
  // what this spec did — was decoration: the string was in neither the response nor the page, so
  // the probe had nothing to find whatever the screen rendered. A mutation that made the row
  // render `JSON.stringify(session)` — the whole DTO, verbatim, the exact leak this spec names —
  // was caught only by a strict-mode locator collision on the line above, never by this one.
  const leaked = [
    { ...CURRENT, tokenHash: 'sha256:0000feed' },
    { ...OTHER, tokenHash: 'sha256:0000beef' },
  ] as unknown as Session[]
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, leaked)

  await page.goto('/settings/sessions')
  await expect(page.getByText('Chrome on Linux').first()).toBeVisible()

  // Positive control on the probe itself: `userAgent` IS rendered, so a value planted there must
  // be found. Without this, a probe that had stopped being able to see the page would report the
  // same silence as a page with nothing to hide.
  await expect(page.locator('body')).toContainText('Safari on iPhone')

  // The real assertion: the screen renders the fields it knows, never the response wholesale.
  await expect(page.locator('body')).not.toContainText('tokenHash')
  await expect(page.locator('body')).not.toContainText('sha256:0000feed')
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
    .getByRole('button', { name: `Actions for the session from ${OTHER.ipAddress}` })
    .click()

  // Positive control: the row's menu opened and holds the command, so a failure below is about the
  // confirmation and not about a menu that never appeared.
  const signOut = page.getByRole('menuitem', { name: 'Sign out' })
  await expect(signOut).toBeVisible()
  await signOut.click()

  // Confirmation first: the row the user clicks may be the session they are reading from. Asserted
  // on the dialog and by its accessible name, so a dialog asking about the wrong device fails.
  const dialog = page.getByRole('dialog', { name: `End the session from ${OTHER.ipAddress}` })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText('End this session?')
  await dialog.getByRole('button', { name: 'End' }).click()

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

// The current device's question is not the other device's question: ending this one signs the
// reader out where they are standing, and the dialog has to say so.
test('the confirmation for this device says it signs you out here', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])

  await page.goto('/settings/sessions')
  await page
    .getByRole('row', { name: /Chrome on Linux/ })
    .getByRole('button', { name: `Actions for the session from ${CURRENT.ipAddress}` })
    .click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()

  const dialog = page.getByRole('dialog', { name: `End the session from ${CURRENT.ipAddress}` })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText('This will sign you out here.')
})

// The answer "no" must leave the device signed in. Counting the DELETEs is what says so: the row
// disappearing is a consequence, not the request.
test('dismissing the session confirmation sends no request, and confirm is not the default answer', async ({
  page,
}) => {
  const deletes: string[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSessions(page, [CURRENT, OTHER])
  page.on('request', (request) => {
    if (request.method() === 'DELETE' && request.url().includes('/api/v1/sessions/')) {
      deletes.push(request.url())
    }
  })

  await page.goto('/settings/sessions')
  await page
    .getByRole('row', { name: /Safari on iPhone/ })
    .getByRole('button', { name: `Actions for the session from ${OTHER.ipAddress}` })
    .click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()

  const dialog = page.getByRole('dialog', { name: `End the session from ${OTHER.ipAddress}` })
  await expect(dialog).toBeVisible()

  // Focus is inside the dialog rather than on the row menu's trigger behind it, and not on the
  // confirm button: a reflexive Enter must not end a session.
  const focus = await page.evaluate(() => {
    const panel = document.querySelector('[role="dialog"]')
    const active = document.activeElement
    return {
      inside: panel !== null && active !== null && panel.contains(active),
      label: active?.getAttribute('aria-label') ?? '',
      text: active?.textContent?.trim() ?? '',
    }
  })
  expect(focus.inside).toBe(true)
  expect(focus.label).not.toContain('Actions for')
  expect(focus.text).not.toEqual('End')

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deletes).toEqual([])
  await expect(page.getByText('Safari on iPhone')).toBeVisible()
})
