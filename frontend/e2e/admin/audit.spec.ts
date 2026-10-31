import { expect, test } from '@playwright/test'
import { stubAudit, stubAuditProblem } from '../fixtures/stub-audit-routes'
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
  actionName: 'Signed in',
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
  actionName: 'Sign-in refused',
  subject: 'admin',
  ipAddress: '198.51.100.4',
  succeeded: false,
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
  await stubAuditProblem(page, 403, 'Administrators only.')

  await page.goto('/settings/audit')

  await expect(page.getByText('Administrators only.')).toBeVisible()
})
