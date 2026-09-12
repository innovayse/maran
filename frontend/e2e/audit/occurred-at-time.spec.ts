import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { AuditEvent } from '../../src/types/audit'

// The journal rendered its When column through `formatDate`, whose pattern is `d MMM yyyy`. Two
// events recorded seconds apart therefore carried the IDENTICAL string, and a day's ordering was
// unreadable off the screen — on the one page in the product whose entire subject is when something
// happened, and against the promise the login screen makes about an immutable record of every
// change. The backups tables beside it had shown the time of day the whole while, through a
// formatter that already existed.
//
// The two entries below are one minute apart on the same day, which is the defect's own shape: under
// the old formatter both rendered `9 сент. 2026` and nothing on screen told them apart.
//
// The instants are fixed UTC and the timezone is pinned, because the panel renders in the browser's
// own zone — the operator's — so an unpinned suite would assert a different clock on every machine.
test.use({ timezoneId: 'UTC' })

/** The later of two entries recorded within a minute of each other. */
const SIGNED_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-09T09:08:00+00:00',
  actorUsername: 'verifier',
  action: 'LoginSucceeded',
  actionName: 'Вход выполнен',
  subject: 'verifier',
  ipAddress: '127.0.0.1',
  succeeded: true,
}

/** The earlier one, one minute before it. */
const ADMIN_CREATED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e2',
  occurredAt: '2026-09-09T09:07:00+00:00',
  actorUsername: 'verifier',
  action: 'AdministratorCreated',
  actionName: 'Создан администратор',
  subject: 'verifier',
  ipAddress: '127.0.0.1',
  succeeded: true,
}

test('the journal states the time of day, so two entries on one day are told apart', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([SIGNED_IN, ADMIN_CREATED]),
    })
  })

  await page.goto('/settings/audit')

  // The VALUES, in Russian, exactly — not "the cell contains a colon" and not "the two cells
  // differ". Both are the strings the neighbouring backups tables already render for an instant.
  await expect(page.getByText('9 сент. 2026 09:08', { exact: true })).toBeVisible()
  await expect(page.getByText('9 сент. 2026 09:07', { exact: true })).toBeVisible()
  // And the date-only form is gone from the screen rather than merely joined by a longer one: a
  // cell still rendering `9 сент. 2026` alone would satisfy both lines above if a second column
  // had grown one.
  await expect(page.getByText('9 сент. 2026', { exact: true })).toHaveCount(0)
})
