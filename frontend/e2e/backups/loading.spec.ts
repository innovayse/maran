import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubBackupDestinations,
  stubBackupDestinationsRefusal,
} from '../fixtures/stub-backup-destination-routes'
import { stubBackups, stubBackupsProblem } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Backup } from '../../src/types/backup'
import type { BackupDestination } from '../../src/types/backupDestination'
import type { PanelModule } from '../../src/types/module'

// The backups table and the destinations screen while their reads are on the way (#243).

const LICENSED: PanelModule[] = [{ name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true }]

const COMPLETED: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  ownerName: 'alice',
  orphanedAccountUsername: '',
  status: 'completed',
  kind: 'manual',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: '',
  failureDisplayName: '',
}

const LOCAL: BackupDestination = {
  id: '77777777-7777-7777-7777-777777777777',
  name: 'This server',
  displayName: 'This server',
  kind: 'local',
  path: '/var/backups/maran',
  isDefault: true,
  createdAt: '2026-09-01T09:00:00Z',
}

/**
 * Signs in with the backups module licensed.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

/** How the backups list screen is opened and answered. */
const BACKUPS_LIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubBackups(page, [COMPLETED])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubBackupsProblem(page, 'The backups could not be read.')
  },
  pattern: /\/api\/v1\/backups(\?.*)?$/,
  path: '/backups',
  table: "Backups taken of this panel's accounts",
  rowText: 'alice',
  failureText: 'The backups could not be read.',
}

test('backups list: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, BACKUPS_LIST_SCENARIO)
})

test('backups list: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, BACKUPS_LIST_SCENARIO)
})

test('backups list: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, BACKUPS_LIST_SCENARIO)
})

/** How the backup destinations screen is opened and answered. */
const DESTINATIONS_LIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubBackupDestinations(page, [LOCAL])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubBackupDestinationsRefusal(page, 403, 'Forbidden', 'This screen is for administrators.')
  },
  pattern: /\/api\/v1\/backup-destinations(\?.*)?$/,
  path: '/backups/destinations',
  table: 'Places this server records its backups as living',
  rowText: '/var/backups/maran',
  failureText: 'This screen is for administrators.',
}

test('destinations list: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, DESTINATIONS_LIST_SCENARIO)
})

test('destinations list: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, DESTINATIONS_LIST_SCENARIO)
})

test('destinations list: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, DESTINATIONS_LIST_SCENARIO)
})
