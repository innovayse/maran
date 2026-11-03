import { expect, test } from '@playwright/test'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { AuditEvent } from '../../src/types/audit'

// The outcome word on a journal row is the panel's own: the row carries it, so the bundle holds no
// copy of the vocabulary (issue #330).

/** A name no bundle contains, so a badge printing it can only have read it off the row. */
const REFUSED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000c2',
  occurredAt: '2026-09-09T08:00:00+00:00',
  actorUsername: 'mallory',
  impersonatorUsername: null,
  action: 'LoginFailed',
  actionName: 'Sign-in refused',
  subject: 'mallory',
  ipAddress: '198.51.100.4',
  succeeded: false,
  outcomeName: 'Panel-named refusal',
}

test('the outcome badge of a journal row prints the name the row carries', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAudit(page, [REFUSED])

  await page.goto('/settings/audit')

  const row = page.getByRole('row').filter({ hasText: REFUSED.ipAddress })
  await expect(row.getByText('Panel-named refusal', { exact: true })).toBeVisible()
})

test('a link naming an outcome the panel does not list is not applied once the list arrives', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAudit(page, [REFUSED])

  await page.goto('/settings/audit?outcome=bogus')

  await expect(page.getByRole('row').filter({ hasText: REFUSED.ipAddress })).toBeVisible()
  await expect(page.getByTestId('active-filter-chips')).toHaveCount(0)
})
