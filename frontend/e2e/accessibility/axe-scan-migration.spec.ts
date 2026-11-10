import { expect, test } from '../fixtures/guarded-test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import {
  CHANGED_SERVER,
  FRESH_SERVER,
  MAIL_WARNING,
  READY_SERVER,
  discoveredAccount,
  openMigrationScreen,
  openRemoteServersList,
} from '../fixtures/migration'
import {
  CRON_FINDING,
  GENERATED_SECRETS,
  GRANT_ID,
  IMPORT_ID,
  NAME_TAKEN_FINDING,
  PHP_WARNING_FINDING,
  SOURCE_FINGERPRINT,
  exportOf,
  grantOf,
  importOf,
  openAccountExport,
  openAccountExports,
  openAccountImport,
  openAccountImports,
  openAccountMoveScreen,
  openExportGrants,
  ALICE_ACCOUNT,
} from '../fixtures/stub-account-move-routes'
import { Buffer } from 'node:buffer'
import type { Page } from '@playwright/test'

// Issue #149 for the migration screens (issue #197): axe runs on the remote servers list, a server
// whose host key waits for confirmation, a working server's accounts and the account dialog, with
// real rows in them, in both themes, and finds no serious or critical violation. A contrast
// failure lives in one theme's tokens and an empty list draws no table at all, so every screen is
// scanned with data. Every screen is reached by clicking. The account export and import screens
// (issue #127) are scanned the same way: the package with its grants and the one-time token dialog,
// the upload form, the dry-run report with a conflict, and the outcome with the one-time credentials.

/**
 * Exports alice and reaches the package's screen.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen is drawn.
 */
const exportAlice = async (page: Page): Promise<void> => {
  await openAccountExport(page)
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'alice' }).click()
  await page.getByRole('button', { name: 'Export account' }).click()
  await expect(page.getByTestId('export-fingerprint')).toBeVisible()
}

/**
 * Uploads the stub's package and reaches the import's screen.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen is drawn.
 */
const stageImport = async (page: Page): Promise<void> => {
  await openAccountImport(page)
  await page.getByLabel('Package file').setInputFiles({
    name: 'alice.maranpkg',
    mimeType: 'application/octet-stream',
    buffer: Buffer.from('MARANPKG1 package bytes for specs'),
  })
  await page.getByRole('textbox', { name: 'Key fingerprint of the sending server' }).fill(SOURCE_FINGERPRINT)
  await page.getByRole('button', { name: 'Upload and verify' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Import of alice' })).toBeVisible()
}

const ACCOUNTS = [discoveredAccount('alice'), discoveredAccount('bob', { mailboxCount: 2 })]

for (const theme of ['dark', 'light'] as const) {
  test.describe(`in the ${theme} theme`, () => {
    test.use({ colorScheme: theme })

    test('the remote servers list has no serious accessibility violation', async ({ page }) => {
      await openMigrationScreen(page, { servers: [FRESH_SERVER, READY_SERVER, CHANGED_SERVER] })

      await openRemoteServersList(page)
      await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('a server with its host key step has no serious accessibility violation', async ({ page }) => {
      await openMigrationScreen(page, { servers: [FRESH_SERVER] })

      await openRemoteServersList(page)
      await page.getByRole('link', { name: 'old-shared', exact: true }).click()
      await expect(page.getByTestId('host-key-step')).toBeVisible()
      await expect(page.getByTestId('authorized-keys-line')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the accounts of a server and an account dialog have no serious accessibility violation', async ({ page }) => {
      const server = { ...READY_SERVER, accountsFound: 2, discoveredAt: '2026-11-03T12:00:00Z', warnings: [MAIL_WARNING] }
      await openMigrationScreen(page, { servers: [server], accounts: { [server.id]: ACCOUNTS } })

      await openRemoteServersList(page)
      await page.getByRole('link', { name: 'legacy-web', exact: true }).click()
      await page.getByRole('button', { name: 'Accounts', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Details of the account alice' })).toBeVisible()
      await expectNoSeriousViolations(page)

      await page.getByRole('button', { name: 'Details of the account alice' }).click()
      await expect(page.getByRole('dialog', { name: 'Account alice' })).toBeVisible()
      await expectNoSeriousViolations(page)
    })

    test('the export form has no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page)

      await openAccountExport(page)
      await expect(page.getByRole('combobox', { name: 'Account' })).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('an export with its grants and the one-time token dialog have no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page)
      await exportAlice(page)
      await page.getByRole('button', { name: 'Issue grant' }).click()
      await expect(page.getByRole('dialog', { name: 'Pull grant issued' })).toBeVisible()
      await expectNoSeriousViolations(page)

      await page.getByRole('button', { name: 'I have copied it' }).click()
      await expect(page.getByRole('link', { name: 'See the grants of alice' })).toBeVisible()
      await expectNoSeriousViolations(page)
    })

    test('the import upload form has no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page)

      await openAccountImport(page)
      await expect(page.getByLabel('Package file')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('a dry-run report with a conflict, a warning and the cron notice has no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page, { findings: [NAME_TAKEN_FINDING, PHP_WARNING_FINDING, CRON_FINDING] })
      await stageImport(page)
      await page.getByRole('combobox', { name: 'Plan' }).click()
      await page.getByRole('option', { name: /Starter/ }).click()
      await page.getByRole('button', { name: 'Run dry run' }).click()
      await expect(page.getByTestId('cron-disabled-notice')).toBeVisible()

      await expectNoSeriousViolations(page)
    })

    test('the exports, imports and pull grants lists have no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page, {
        exports: [exportOf(ALICE_ACCOUNT)],
        imports: [
          importOf({ id: IMPORT_ID, state: 'Running', stage: 'sites' }),
          importOf({ id: 'b2b2b2b2-0000-4000-8000-000000000002', accountName: 'bob', state: 'RolledBack', failureCode: 'MigrationRolledBack', failureName: 'The import was rolled back.' }),
        ],
        grants: [grantOf({ id: GRANT_ID }), grantOf({ id: 'a1a1a1a1-0000-4000-8000-000000000002', state: 'Used' })],
      })

      await openAccountExports(page)
      await expect(page.getByRole('link', { name: 'alice', exact: true })).toBeVisible()
      await expectNoSeriousViolations(page)

      await openAccountImports(page)
      await expect(page.getByRole('link', { name: 'bob', exact: true })).toBeVisible()
      await expectNoSeriousViolations(page)

      await openExportGrants(page)
      await expect(page.getByRole('button', { name: 'Actions for the grant of alice' })).toBeVisible()
      await expectNoSeriousViolations(page)
    })

    test('a completed import and its one-time credentials have no serious accessibility violation', async ({ page }) => {
      await openAccountMoveScreen(page, { findings: [CRON_FINDING] })
      await stageImport(page)
      await page.getByRole('combobox', { name: 'Plan' }).click()
      await page.getByRole('option', { name: /Starter/ }).click()
      await page.getByRole('button', { name: 'Run dry run' }).click()
      const step = page.getByTestId('import-confirm-step')
      await step.getByRole('textbox', { name: 'Account name' }).fill('alice')
      await step.getByRole('textbox', { name: 'Key fingerprint' }).fill(SOURCE_FINGERPRINT)
      await page.getByRole('button', { name: 'Start import' }).click()
      await expect(page.getByText('The account alice was imported.')).toBeVisible({ timeout: 15_000 })
      await expectNoSeriousViolations(page)

      await page.getByRole('button', { name: 'Show the credentials once' }).click()
      await expect(page.getByTestId('issued-secret')).toHaveCount(GENERATED_SECRETS.length)
      await expectNoSeriousViolations(page)
    })
  })
}
