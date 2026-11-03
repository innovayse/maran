import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups, stubBackupVerification, type BackupVerifyAnswer } from '../fixtures/stub-backups-routes'
import { stubBackupOptions } from '../fixtures/stub-backup-options-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', state: 'enabled' },
]

/**
 * Builds an account the three backups below belong to.
 * @param id The account's identity.
 * @param name The account's short name, which a row names.
 * @returns The account.
 */
const accountNamed = (id: string, name: string): Account => {
  return {
    id,
    name,
    primaryDomain: `${name}.example.com`,
    planId: '44444444-4444-4444-4444-444444444444',
    status: 'active',
    statusName: 'Active',
    createdAt: '2026-08-01T10:00:00Z',
    resellerId: null,
    resellerName: null,
  }
}

const ALICE = accountNamed('22222222-2222-2222-2222-222222222222', 'alice')
const BOB = accountNamed('33333333-3333-3333-3333-333333333333', 'bob')
const CAROL = accountNamed('55555555-5555-5555-5555-555555555555', 'carol')

/**
 * A completed backup of an account that nobody has verified yet, the state of every row taken
 * before verification existed.
 * @param id The backup's identity.
 * @param account The account it is a copy of.
 * @returns The backup.
 */
const completedFor = (id: string, account: Account): Backup => {
  return {
    id,
    accountId: account.id,
    ownerName: account.name,
    orphanedAccountUsername: null,
    status: 'completed',
    statusName: 'Completed',
    kind: 'manual',
    kindName: 'Manual',
    sizeBytes: 1_572_864,
    sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
    databaseCount: 2,
    startedAt: '2026-09-05T03:00:00Z',
    finishedAt: '2026-09-05T03:02:10Z',
    failureCode: null,
    failureDisplayName: null,
    verificationState: 'notVerified',
    verificationStateName: 'Not verified',
    verifiedAt: null,
    verificationFailure: null,
  }
}

const UNVERIFIED = completedFor('11111111-1111-1111-1111-111111111111', ALICE)

const VERIFIED: Backup = {
  ...completedFor('11111111-1111-1111-1111-222222222222', BOB),
  verificationState: 'verified',
  verificationStateName: 'Verified',
  verifiedAt: '2026-09-05T03:03:00Z',
}

const UNSOUND: Backup = {
  ...completedFor('11111111-1111-1111-1111-333333333333', CAROL),
  verificationState: 'failed',
  verificationStateName: 'Verification failed',
  verifiedAt: '2026-09-05T03:03:00Z',
  verificationFailure: {
    code: 'BackupArtifactChecksumMismatch',
    message: 'The archive no longer matches the checksum recorded when it was taken.',
  },
}

/**
 * Opens the backups screen over the given rows, with the verify endpoint answering as stated.
 * @param page The Playwright page.
 * @param backups The rows the stub holds.
 * @param answer What the panel answers a verification with.
 * @returns A function answering the backup ids verified so far.
 */
const openBackups = async (
  page: Page,
  backups: Backup[],
  answer: BackupVerifyAnswer,
): Promise<() => string[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubBackupOptions(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB, CAROL])
  await stubBackups(page, backups)
  const verified = await stubBackupVerification(page, backups, answer)
  await page.goto('/backups')
  return verified
}

const SOUND_NOW: BackupVerifyAnswer = {
  kind: 'record',
  changes: {
    verificationState: 'verified',
    verificationStateName: 'Verified',
    verifiedAt: '2026-09-07T10:00:00Z',
    verificationFailure: null,
  },
}

test('each row shows its verification state in the server own words', async ({ page }) => {
  await openBackups(page, [UNVERIFIED, VERIFIED, UNSOUND], SOUND_NOW)

  await expect(page.getByRole('row').filter({ hasText: 'alice' }).getByTestId('backup-verification')).toHaveText('Not verified')
  await expect(page.getByRole('row').filter({ hasText: 'bob' }).getByTestId('backup-verification')).toHaveText('Verified')
  await expect(page.getByRole('row').filter({ hasText: 'carol' }).getByTestId('backup-verification')).toHaveText('Verification failed')
})

test('a failed verification prints the backend message and its code beneath it', async ({ page }) => {
  await openBackups(page, [UNSOUND], SOUND_NOW)

  const row = page.getByRole('row').filter({ hasText: 'carol' })
  await expect(row).toContainText('The archive no longer matches the checksum recorded when it was taken.')
  await expect(row).toContainText('Code: BackupArtifactChecksumMismatch')
})

test('Verify now flips a not-verified row to verified and sends one request', async ({ page }) => {
  const verified = await openBackups(page, [UNVERIFIED], SOUND_NOW)

  const badge = page.getByRole('row').filter({ hasText: 'alice' }).getByTestId('backup-verification')
  await expect(badge).toHaveText('Not verified')

  await page.getByRole('button', { name: 'Verify now: backup of alice' }).click()

  await expect(badge).toHaveText('Verified')
  expect(verified()).toEqual([UNVERIFIED.id])
})

test('Verify now shows a failed verification message verbatim', async ({ page }) => {
  const message = 'The archive is missing the dump of the database shop.'
  await openBackups(page, [UNVERIFIED], {
    kind: 'record',
    changes: {
      verificationState: 'failed',
      verificationStateName: 'Verification failed',
      verifiedAt: '2026-09-07T10:00:00Z',
      verificationFailure: { code: 'BackupDumpMissing', message },
    },
  })

  await page.getByRole('button', { name: 'Verify now: backup of alice' }).click()

  const row = page.getByRole('row').filter({ hasText: 'alice' })
  await expect(row.getByTestId('backup-verification')).toHaveText('Verification failed')
  await expect(row).toContainText(message)
  await expect(row).toContainText('Code: BackupDumpMissing')
})

test('a refused verification shows the panel message above the list', async ({ page }) => {
  const detail = 'The backup agent is not reachable.'
  await openBackups(page, [UNVERIFIED], { kind: 'problem', status: 503, code: 'AgentUnavailable', detail })

  await page.getByRole('button', { name: 'Verify now: backup of alice' }).click()

  await expect(page.getByText(detail)).toBeVisible()
  // The row is unchanged: a refusal says nothing about the artifact.
  await expect(page.getByRole('row').filter({ hasText: 'alice' }).getByTestId('backup-verification')).toHaveText('Not verified')
})

test('the Verify now button is disabled while its request is in flight', async ({ page }) => {
  await openBackups(page, [UNVERIFIED], SOUND_NOW)
  // Hold the answer so the busy state can be observed, then release it.
  let release: () => void = () => undefined
  const gate = new Promise<void>((resolve) => {
    release = resolve
  })
  await page.route('**/api/v1/backups/*/verify', async (route) => {
    await gate
    await route.fallback()
  })

  await page.getByRole('button', { name: 'Verify now: backup of alice' }).click()
  await expect(page.getByRole('button', { name: 'Verify now: backup of alice' })).toBeDisabled()

  release()
  await expect(page.getByRole('row').filter({ hasText: 'alice' }).getByTestId('backup-verification')).toHaveText('Verified')
})

test('a backup that is not completed offers no Verify now', async ({ page }) => {
  const failedRun: Backup = {
    ...UNVERIFIED,
    status: 'failed',
    statusName: 'Failed',
    sizeBytes: 0,
    sha256: null,
    databaseCount: 0,
    failureCode: 'BackupArchiveTooLarge',
    failureDisplayName: 'The archive grew past what this server will store',
  }
  await openBackups(page, [failedRun, VERIFIED], SOUND_NOW)

  // Positive control: the completed row IS on the page and does offer the button.
  await expect(page.getByRole('button', { name: 'Verify now: backup of bob' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Verify now: backup of alice' })).toHaveCount(0)
})

test.describe('chrome in each language', () => {
  const chrome = [
    { locale: 'ru', button: 'Проверить сейчас: копия alice', title: 'Проверена' },
    { locale: 'hy', button: 'Ստուգել հիմա՝ alice-ի պատճենը', title: 'Ստուգվել է' },
  ] as const

  for (const { locale, button, title } of chrome) {
    test(`the ${locale} interface translates the button and keeps the server state names as sent`, async ({ page }) => {
      await setPersistedLocale(page, locale)
      const named: Backup = { ...VERIFIED, verificationStateName: `server-${locale}-verified` }
      await openBackups(page, [UNVERIFIED, named], SOUND_NOW)

      await expect(page.getByRole('button', { name: button })).toBeVisible()
      const row = page.getByRole('row').filter({ hasText: 'bob' })
      await expect(row.getByTestId('backup-verification')).toHaveText(`server-${locale}-verified`)
      await expect(row).toContainText(title)
    })
  }
})

test.describe('at 390px', () => {
  for (const locale of ['en', 'ru', 'hy'] as const) {
    test(`the ${locale} backups list does not scroll sideways`, async ({ page }) => {
      await page.setViewportSize({ width: 390, height: 800 })
      await setPersistedLocale(page, locale)
      await openBackups(page, [UNVERIFIED, VERIFIED, UNSOUND], SOUND_NOW)

      // Positive control: the failure sentence IS rendered, so the measurement covers the long text.
      await expect(page.getByText('The archive no longer matches the checksum')).toBeVisible()
      const widths = await page.evaluate(() => {
        return { document: document.documentElement.scrollWidth, window: window.innerWidth }
      })
      expect(widths.document).toBeLessThanOrEqual(widths.window)
    })
  }
})
