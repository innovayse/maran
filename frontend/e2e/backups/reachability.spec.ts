import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubBackupDestinations } from '../fixtures/stub-backup-destination-routes'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

// The schedule screen shipped with its store, its API composable and its types, and no route: no
// URL led to it and no link named it, so the whole scheduling feature — and the retention pruning
// behind it — was dead in the browser. Every spec it had still passed, because a spec that opens a
// URL says nothing about whether a person can get there.
//
// So this spec never types a URL. It starts where an operator starts, on the backups list, and
// walks. A route added without a way in fails here; a link added without a route fails here; and
// `npm run lint` fails on the static half (`scripts/check-page-routes.mjs`) for any page nobody
// routes at all, including the ones no spec has been written for.
test('an operator reaches the schedule and the destinations by clicking, not by typing a URL', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [])
  await stubBackupSchedules(page, {})
  await stubBackupDestinations(page, [])

  await page.goto('/backups')

  // Positive control on the probe: the list screen IS what loaded, so a failure below is a missing
  // way in rather than a page that never rendered.
  await expect(page.getByRole('heading', { name: 'Backups' })).toBeVisible()

  await page.getByRole('link', { name: 'Backup schedule' }).click()
  await expect(page).toHaveURL(/\/backups\/schedule$/)
  await expect(page.getByRole('heading', { name: 'Backup schedule' })).toBeVisible()

  await page.goBack()
  await page.getByRole('link', { name: 'Backup destinations' }).click()
  await expect(page).toHaveURL(/\/backups\/destinations$/)
  await expect(page.getByRole('heading', { name: 'Backup destinations' })).toBeVisible()
})
