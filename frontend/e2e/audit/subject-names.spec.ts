import { expect, test } from '../fixtures/guarded-test'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuditEvent } from '../../src/types/audit'

// The subject of a journal row is the panel's own text: the row carries a localized sentence for an
// entry whose stored subject is a machine line, and the bundle neither parses nor translates it
// (issue #509).

/** A chain verdict: the stored line is raw, the name is the sentence the backend spelled. */
const VERDICT: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000d1',
  occurredAt: '2026-09-09T08:00:00+00:00',
  actorUsername: 'admin',
  impersonatorUsername: null,
  action: 'AuditChainVerified',
  actionName: 'Audit chain verified',
  subject: 'intact rowsChecked=164',
  subjectName: 'Panel-spelled verdict, 164 rows',
  ipAddress: '203.0.113.7',
  succeeded: true,
  outcomeName: 'Succeeded',
}

/** A sign-out whose subject is the person who signed out, not their id. */
const SIGN_OUT: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000d2',
  occurredAt: '2026-09-09T08:05:00+00:00',
  actorUsername: 'edgar',
  impersonatorUsername: null,
  action: 'LoggedOut',
  actionName: 'Signed out',
  subject: 'edgar',
  subjectName: 'edgar',
  ipAddress: '198.51.100.4',
  succeeded: true,
  outcomeName: 'Succeeded',
}

test('the subject cell prints the name the row carries and never the stored machine line', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubAudit(page, [VERDICT, SIGN_OUT])

  await page.goto('/settings/audit')

  const verdict = page.getByRole('row').filter({ hasText: VERDICT.ipAddress })
  await expect(verdict.getByText('Panel-spelled verdict, 164 rows', { exact: true })).toBeVisible()
  await expect(verdict.getByText('rowsChecked', { exact: false })).toHaveCount(0)
})

test('a subject that is already a name is printed as it is', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubAudit(page, [VERDICT, SIGN_OUT])

  await page.goto('/settings/audit')

  const signOut = page.getByRole('row').filter({ hasText: SIGN_OUT.ipAddress })
  await expect(signOut.getByText('edgar', { exact: true }).first()).toBeVisible()
})
