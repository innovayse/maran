import { Buffer } from 'node:buffer'
import { expect, test } from '../fixtures/guarded-test'
import {
  CRON_FINDING,
  GENERATED_SECRETS,
  NAME_TAKEN_FINDING,
  PHP_WARNING_FINDING,
  PLAN_ID,
  ROLLED_BACK_CODE,
  ROLLED_BACK_NAME,
  SOURCE_FINGERPRINT,
  openAccountImport,
  openAccountMoveScreen,
  type AccountMoveStubState,
} from '../fixtures/stub-account-move-routes'
import type { Page } from '@playwright/test'

// Moving an account between Maran servers, the import side (issue #127). Every screen is reached by
// clicking from the sidebar down. The SPA's checks are advice and the panel is law, so each refusing
// control has an inverse (the input it must accept), and the dry-run and the typed confirmation are
// exercised against a stub that enforces what the panel enforces.

/**
 * Uploads the stub's package with a fingerprint, the way a person does.
 * @param page The Playwright page under test.
 * @param fingerprint The fingerprint to type.
 * @returns Resolves once the upload was submitted.
 */
const upload = async (page: Page, fingerprint: string = SOURCE_FINGERPRINT): Promise<void> => {
  await page.getByLabel('Package file').setInputFiles({
    name: 'alice.maranpkg',
    mimeType: 'application/octet-stream',
    buffer: Buffer.from('MARANPKG1 package bytes for specs'),
  })
  await page.getByRole('textbox', { name: 'Key fingerprint of the sending server' }).fill(fingerprint)
  await page.getByRole('button', { name: 'Upload and verify' }).click()
}

/**
 * Opens the import screen, uploads the package and waits for the import's own screen.
 * @param page The Playwright page under test.
 * @param seed What the stub starts with.
 * @returns The stub's live state.
 */
const stageImport = async (page: Page, seed: Partial<AccountMoveStubState> = {}): Promise<AccountMoveStubState> => {
  const state = await openAccountMoveScreen(page, seed)
  await openAccountImport(page)
  await upload(page)
  await expect(page.getByRole('heading', { level: 1, name: 'Import of alice' })).toBeVisible()
  return state
}

/**
 * Chooses the stub's only plan and runs the dry run.
 * @param page The Playwright page under test.
 * @returns Resolves once the report is drawn.
 */
const runDryRun = async (page: Page): Promise<void> => {
  await page.getByRole('combobox', { name: 'Plan' }).click()
  await page.getByRole('option', { name: /Starter/ }).click()
  await page.getByRole('button', { name: 'Run dry run' }).click()
  await expect(page.getByTestId('report-verdict')).toBeVisible()
}

/**
 * Types both confirmations, correctly.
 * @param page The Playwright page under test.
 * @returns Resolves once both fields hold their text.
 */
const confirmBoth = async (page: Page): Promise<void> => {
  const step = page.getByTestId('import-confirm-step')
  await step.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await step.getByRole('textbox', { name: 'Key fingerprint' }).fill(SOURCE_FINGERPRINT)
}

test.describe('the upload form', () => {
  test('uploads the package with the typed fingerprint and opens the import with what the package says', async ({ page }) => {
    const state = await stageImport(page)

    await expect(page.getByText('old-panel.example.net', { exact: true })).toBeVisible()
    await expect(page.getByText('Waiting to start')).toBeVisible()
    expect(state.uploads).toHaveLength(1)
    expect(state.uploads[0]?.fingerprint).toBe(SOURCE_FINGERPRINT)
    expect(state.uploads[0]?.bytes).toBeGreaterThan(0)
  })

  test("shows the panel's refusal when the key is not the one that signed the package, and stays on the form", async ({ page, guard }) => {
    guard.allow(400, /\/api\/v1\/account-imports\?/)
    await openAccountMoveScreen(page)
    await openAccountImport(page)

    await upload(page, 'SHA256:someOtherKeyEntirely')

    await expect(page.getByText('The package was not signed by the key with that fingerprint.')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Import an account' })).toBeVisible()
  })

  test('asks for the file and the fingerprint before it uploads anything, and accepts both once given', async ({ page }) => {
    const state = await openAccountMoveScreen(page)
    await openAccountImport(page)

    await page.getByRole('button', { name: 'Upload and verify' }).click()

    await expect(page.getByText('Choose the package file.')).toBeVisible()
    await expect(page.getByText('Enter the key fingerprint.')).toBeVisible()
    expect(state.uploads).toHaveLength(0)

    // The inverse: with both given the same control uploads.
    await upload(page)
    await expect(page.getByRole('heading', { level: 1, name: 'Import of alice' })).toBeVisible()
    expect(state.uploads).toHaveLength(1)
  })
})

test.describe('the dry run', () => {
  test('names every finding, blocks the start while a conflict stands, and shows the cron notice apart', async ({ page }) => {
    const state = await stageImport(page, { findings: [NAME_TAKEN_FINDING, PHP_WARNING_FINDING, CRON_FINDING] })

    await runDryRun(page)

    await expect(page.getByTestId('report-verdict')).toContainText('The import cannot start while a conflict remains')
    await expect(page.getByText(NAME_TAKEN_FINDING.message)).toBeVisible()
    await expect(page.getByText(PHP_WARNING_FINDING.message)).toBeVisible()
    await expect(page.getByTestId('cron-disabled-notice')).toContainText(CRON_FINDING.message)
    // Even with both confirmations typed the start stays off: the panel would refuse it as well.
    await confirmBoth(page)
    await expect(page.getByRole('button', { name: 'Start import' })).toBeDisabled()
    const report = state.requests.find((request) => {
      return request.url.pathname.endsWith('/report')
    })
    expect(report?.url.searchParams.get('planId')).toBe(PLAN_ID)
  })

  test('says nothing conflicts when the report is clean, and lets the start through', async ({ page }) => {
    await stageImport(page, { findings: [] })

    await runDryRun(page)

    await expect(page.getByTestId('report-verdict')).toContainText('No conflicts. The import can start.')
    await confirmBoth(page)
    await expect(page.getByRole('button', { name: 'Start import' })).toBeEnabled()
  })

  test('does not run the dry run before a plan is chosen', async ({ page }) => {
    await stageImport(page)

    await expect(page.getByRole('button', { name: 'Run dry run' })).toBeDisabled()
  })
})

test.describe('the typed confirmation', () => {
  test('keeps the start off until the name and the fingerprint are both retyped exactly', async ({ page }) => {
    await stageImport(page, { findings: [CRON_FINDING] })
    await runDryRun(page)
    const step = page.getByTestId('import-confirm-step')
    const start = page.getByRole('button', { name: 'Start import' })
    await expect(start).toBeDisabled()

    await step.getByRole('textbox', { name: 'Account name' }).fill('Alice')
    await step.getByRole('textbox', { name: 'Key fingerprint' }).fill(SOURCE_FINGERPRINT)
    await expect(step.getByText('This does not match.')).toBeVisible()
    await expect(start).toBeDisabled()

    // The inverse: the exact name goes through; surrounding spaces around a pasted fingerprint do not matter.
    await step.getByRole('textbox', { name: 'Account name' }).fill('alice')
    await step.getByRole('textbox', { name: 'Key fingerprint' }).fill(`  ${SOURCE_FINGERPRINT}  `)
    await expect(start).toBeEnabled()
  })

  test('sends what was typed with the chosen plan, and the panel starts the job', async ({ page }) => {
    const state = await stageImport(page, { findings: [CRON_FINDING] })
    await runDryRun(page)
    await confirmBoth(page)

    await page.getByRole('button', { name: 'Start import' }).click()

    await expect(page.getByTestId('import-progress')).toBeVisible()
    const started = state.requests.find((request) => {
      return request.method === 'POST' && request.url.pathname.endsWith('/start')
    })
    expect(started?.body).toEqual({
      planId: PLAN_ID,
      resellerId: null,
      confirmAccountName: 'alice',
      confirmKeyFingerprint: SOURCE_FINGERPRINT,
    })
  })

  test('sends the reseller that is to own the account, and asks for that reseller\'s plans', async ({ page }) => {
    const state = await stageImport(page, { findings: [] })

    await page.getByRole('combobox', { name: 'Reseller (optional)' }).click()
    await page.getByRole('option', { name: 'Acme Hosting' }).click()
    await runDryRun(page)
    await confirmBoth(page)
    await page.getByRole('button', { name: 'Start import' }).click()

    await expect(page.getByTestId('import-progress')).toBeVisible()
    const started = state.requests.find((request) => {
      return request.method === 'POST' && request.url.pathname.endsWith('/start')
    })
    expect(started?.body).toHaveProperty('resellerId', expect.any(String))
  })
})

test.describe('the job', () => {
  test('is followed to completion, then hands over the generated credentials once', async ({ page }) => {
    await stageImport(page, { findings: [CRON_FINDING, PHP_WARNING_FINDING] })
    await runDryRun(page)
    await confirmBoth(page)
    await page.getByRole('button', { name: 'Start import' }).click()
    await expect(page.getByTestId('import-progress')).toBeVisible()

    await expect(page.getByText('The account alice was imported.')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('cron-disabled-notice')).toContainText(CRON_FINDING.message)
    await expect(page.getByRole('link', { name: 'Open the account' })).toBeVisible()

    await page.getByRole('button', { name: 'Show the credentials once' }).click()
    const dialog = page.getByRole('dialog', { name: 'Generated credentials' })
    await expect(dialog.getByTestId('issued-secret')).toHaveText(GENERATED_SECRETS.map((entry) => {return entry.secret}))
    // Escape must not destroy the only copy of the credentials.
    await page.keyboard.press('Escape')
    await expect(dialog).toBeVisible()

    await dialog.getByRole('button', { name: 'I have copied them' }).click()
    await expect(dialog).toHaveCount(0)
    // The positive control above proves the search can find a credential; here it must find nothing.
    for (const entry of GENERATED_SECRETS) {
      await expect(page.getByText(entry.secret)).toHaveCount(0)
    }
    await expect(page.getByRole('button', { name: 'Show the credentials once' })).toHaveCount(0)
  })

  test("shows a rolled-back import with the panel's sentence for the failure and its code beneath, and offers no credentials", async ({ page }) => {
    await stageImport(page, { failImport: true })
    await runDryRun(page)
    await confirmBoth(page)
    await page.getByRole('button', { name: 'Start import' }).click()

    await expect(page.getByText('The import failed and was rolled back.', { exact: false })).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('import-failure-name')).toHaveText(ROLLED_BACK_NAME)
    await expect(page.getByText(`Code: ${ROLLED_BACK_CODE}`)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Show the credentials once' })).toHaveCount(0)
  })

  test("shows the panel's words when it refuses the start", async ({ page, guard }) => {
    guard.allow(409, /\/api\/v1\/account-imports\/.+\/start$/)
    await stageImport(page, {
      refusals: { 'POST account-imports/:id/start': { detail: 'The plan has no room for this account.' } },
    })
    await runDryRun(page)
    await confirmBoth(page)

    await page.getByRole('button', { name: 'Start import' }).click()

    await expect(page.getByText('The plan has no room for this account.')).toBeVisible()
    await expect(page.getByTestId('import-progress')).toHaveCount(0)
  })
})

test.describe('discarding', () => {
  test('discards the package only after the confirmation, and keeps it when the confirmation is cancelled', async ({ page }) => {
    const state = await stageImport(page)
    const deletes = (): number => {
      return state.requests.filter((request) => {
        return request.method === 'DELETE'
      }).length
    }

    await page.getByRole('button', { name: 'Discard package' }).click()
    const dialog = page.getByRole('dialog', { name: 'Discard this package?' })
    await dialog.getByRole('button', { name: 'Cancel' }).click()
    // The dialog leaves with a transition; until it is gone its confirm button shares the opener's name.
    await expect(dialog).toBeHidden()
    expect(deletes()).toBe(0)

    await page.getByRole('button', { name: 'Discard package' }).click()
    await page.getByRole('dialog', { name: 'Discard this package?' }).getByRole('button', { name: 'Discard package' }).click()

    await expect(page.getByText('This package was discarded. Nothing was created.')).toBeVisible()
    expect(deletes()).toBe(1)
  })

  test('is the not-found state once the panel no longer has the import', async ({ page, guard }) => {
    // The refusal is the subject: the reload reads an import the panel no longer holds.
    guard.allow(404, /\/api\/v1\/account-imports\/[^/]+$/)
    const state = await stageImport(page)
    state.imports = []

    await page.reload()

    await expect(page.getByText('This import is not available')).toBeVisible()
  })
})
