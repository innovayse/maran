import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'

// The status and reason words on the backup history are the panel's own: the rows carry them, so
// the bundle holds no copy of the vocabulary (issue #330).

const LICENSED: PanelModule[] = [{ name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** Names no bundle contains, so a screen printing one can only have read it off the panel's answer. */
const BACKUP: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  orphanedAccountUsername: null,
  status: 'completed',
  statusName: 'Panel-named finished',
  kind: 'preDeletion',
  kindName: 'Panel-named final copy',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: null,
  failureDisplayName: null,
  verificationState: 'notVerified',
  verificationStateName: 'Not verified',
  verifiedAt: null,
  verificationFailureCode: null,
  verificationFailureDisplayName: null,
}

test('the history prints the status and the reason under the names the rows carry', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [BACKUP])

  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: 'alice' })
  await expect(row.getByTestId('backup-status')).toHaveText('Panel-named finished')
  await expect(row.getByText('Panel-named final copy', { exact: true })).toBeVisible()
})
