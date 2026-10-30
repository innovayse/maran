import { earlySkeletons, recordSkeletonFlashes } from '../fixtures/skeleton-flash'
import { expect, test, type Page } from '@playwright/test'
import { delayRoute } from '../fixtures/delay-route'
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
  pattern: '**/api/v1/backups',
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

// The destinations are cards rather than a table, so the wait for them is one card's placeholder
// beside the two cards that describe this build.
test('destinations: a slow answer shows a card placeholder, then the destination', async ({ page }) => {
  await signIn(page)
  await stubBackupDestinations(page, [LOCAL])
  await delayRoute(page, '**/api/v1/backup-destinations', 600)
  await page.goto('/backups/destinations')

  await expect(page.locator('[data-skeleton="card"]')).toBeVisible()
  await expect(page.getByTestId('loading-status')).toHaveText('Loading…')

  await expect(page.getByRole('heading', { name: 'This server' })).toBeVisible()
  await expect(page.getByTestId('loading-status')).toHaveText('Loaded')
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
})

test('destinations: a fast answer shows no placeholder', async ({ page }) => {
  await signIn(page)
  await stubBackupDestinations(page, [LOCAL])
  await delayRoute(page, '**/api/v1/backup-destinations', 50)
  await recordSkeletonFlashes(page)
  await page.goto('/backups/destinations')

  // Every frame from the first, for longer than the answer and the 150 ms delay together: no
  // placeholder may be seen in the first 150 ms of its wait, although they are mounted to hold
  // the space (see `recordSkeletonFlashes` for why the wait, not the stub, is the clock).
  await page.waitForTimeout(400)
  expect(await earlySkeletons(page)).toEqual([])
  await expect(page.getByRole('heading', { name: 'This server' })).toBeVisible()
})

test('destinations: a slow refusal is an alert, never a skeleton', async ({ page }) => {
  await signIn(page)
  await stubBackupDestinationsRefusal(page, 403, 'Forbidden', 'This screen is for administrators.')
  await delayRoute(page, '**/api/v1/backup-destinations', 600)
  await page.goto('/backups/destinations')

  await expect(page.getByText('This screen is for administrators.')).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
})
