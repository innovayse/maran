import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSessions } from '../fixtures/stub-sessions-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
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

test('the sessions screen lists the devices and marks the one being used', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubSessions(page, [CURRENT, OTHER])

  await page.goto('/settings/sessions')

  await expect(page.getByText('Chrome on Linux')).toBeVisible()
  await expect(page.getByText('Safari on iPhone')).toBeVisible()
  await expect(page.getByText('This device')).toBeVisible()
})

test('a listed session shows its device and never draws the handle that ends it', async ({ page }) => {
  // The session `id` is the only opaque handle the list carries: the revoke call addresses a row by
  // it, so it is the one value on the wire that must stay out of the markup. The stub carries it in
  // that contract field (the guard refuses any member the contract has no room for), with a value
  // that is easy to find, so the absence below is about the screen and not about a probe that
  // could not see it.
  const handles = ['5e551000-0000-4000-8000-00000000c0de', '5e551000-0000-4000-8000-00000000dead']
  const listed: Session[] = [
    { ...CURRENT, id: handles[0] },
    { ...OTHER, id: handles[1] },
  ]
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubSessions(page, listed)

  await page.goto('/settings/sessions')

  // Positive control on the probe: the row built from that same payload IS drawn, and so are the
  // fields it declares, so the absences below are the screen's choice and not a page that failed.
  await expect(page.getByText('Chrome on Linux').first()).toBeVisible()
  await expect(page.locator('body')).toContainText('Safari on iPhone')
  await expect(page.locator('body')).toContainText(CURRENT.ipAddress)

  // The handle appears in neither the text nor any attribute of the drawn page.
  const markup = await page.content()
  for (const handle of handles) {
    expect(markup).not.toContain(handle)
  }
})

test('ending another device asks first, then reads the page again without its row', async ({ page }) => {
  // The panel's list, which the stubbed DELETE changes: the row disappears because the page asked
  // the panel again after the revocation, not because the SPA dropped it locally.
  const listed = [CURRENT, OTHER]
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  const listRequests = await stubSessions(page, listed)
  await page.route(`**/api/v1/sessions/${OTHER.id}`, async (route) => {
    listed.splice(listed.indexOf(OTHER), 1)
    await route.fulfill({ status: 204 })
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
  await dialog.getByRole('button', { name: 'End session' }).click()

  await expect(page.getByText('Safari on iPhone')).toBeHidden()
  await expect(page.getByText('Chrome on Linux')).toBeVisible()
  expect(listRequests().length).toBeGreaterThanOrEqual(2)
})

test('signing out everywhere returns to the sign-in screen', async ({ page, guard }) => {
  // The renewal is made to fail on purpose below, so that nothing can keep the visitor signed in.
  guard.allow(401, /\/api\/v1\/auth\/refresh$/)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubSessions(page, [CURRENT, OTHER])
  await page.route('**/api/v1/auth/logout-all', async (route) => {
    await route.fulfill({ status: 204 })
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
  await stubShellReads(page)
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
  await stubShellReads(page)
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
  expect(focus.text).not.toEqual('End session')

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deletes).toEqual([])
  await expect(page.getByText('Safari on iPhone')).toBeVisible()
})
