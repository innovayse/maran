import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import type { AuditEvent } from '../../src/types/audit'

/** A successful sign-in, as the journal records one. */
const SIGN_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000b1',
  occurredAt: '2026-08-30T09:00:00+00:00',
  actorUsername: 'admin',
  action: 'LoginSucceeded',
  subject: 'admin',
  ipAddress: '203.0.113.7',
  succeeded: true,
}

/** A refused one. Failures are recorded as fully as successes — that is the point of the journal. */
const REFUSED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000b2',
  occurredAt: '2026-08-30T08:59:00+00:00',
  actorUsername: 'admin',
  action: 'LoginFailed',
  subject: 'admin',
  ipAddress: '198.51.100.4',
  succeeded: false,
}

/**
 * Fulfils `GET /api/v1/audit` with a chosen journal.
 * @param page The Playwright page whose network the route is installed on.
 * @param events The entries the panel reports.
 * @returns Resolves once the route is installed.
 */
const stubAudit = async (page: Page, events: AuditEvent[]): Promise<void> => {
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(events) })
  })
}

test('the audit screen lists what happened, and marks a refusal as one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAudit(page, [SIGN_IN, REFUSED])

  await page.goto('/settings/audit')

  await expect(page.getByText('LoginSucceeded')).toBeVisible()
  await expect(page.getByText('LoginFailed')).toBeVisible()
  await expect(page.getByText('Failed', { exact: true })).toBeVisible()
  await expect(page.getByText('203.0.113.7')).toBeVisible()
})

test('an empty journal says so instead of rendering an empty table', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAudit(page, [])

  await page.goto('/settings/audit')

  await expect(page.getByText('Nothing recorded yet')).toBeVisible()
})

test('a refusal from the panel is shown verbatim rather than an empty screen', async ({ page }) => {
  // A customer reaching this URL: the endpoint is administrators-only, and the SPA renders the
  // panel's own message rather than deciding for itself what the visitor may see.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Administrators only.', detail: 'Administrators only.', code: 'Forbidden' }),
    })
  })

  await page.goto('/settings/audit')

  await expect(page.getByText('Administrators only.')).toBeVisible()
})
