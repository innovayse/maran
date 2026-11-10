import { readFile } from 'node:fs/promises'
import { expect, test } from '../fixtures/guarded-test'
import { stubAudit, stubAuditExport, stubAuditExportRefused } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuditEvent } from '../../src/types/audit'
import type { Page } from '@playwright/test'

// The journal's export (#145): everything the current search and filters match, as CSV or JSON,
// downloaded through the panel's authenticated client and saved under the name the panel proposes.

const SIGN_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000d1',
  occurredAt: '2026-09-10T09:00:00+00:00',
  actorUsername: 'admin',
  impersonatorUsername: null,
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'admin',
  subjectName: 'admin',
  ipAddress: '203.0.113.7',
  succeeded: true,
  outcomeName: 'Succeeded',
}

const REFUSED: AuditEvent = {
  ...SIGN_IN,
  id: '00000000-0000-0000-0000-0000000000d2',
  actorUsername: 'mallory',
  action: 'LoginFailed',
  actionName: 'Sign-in refused',
  subject: 'mallory',
  subjectName: 'mallory',
  ipAddress: '198.51.100.4',
  succeeded: false,
  outcomeName: 'Failed',
}

/** The CSV body the stubbed panel streams; the spec reads it back from the saved file. */
const CSV_BODY = 'id,occurredAt\r\n00000000-0000-0000-0000-0000000000d1,2026-09-10T09:00:00Z\r\n'

/**
 * Signs an administrator in over a two-entry journal.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const openJournal = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubAudit(page, [SIGN_IN, REFUSED])
}

test('the export menu downloads the file under the name the panel proposes', async ({ page }) => {
  await openJournal(page)
  const requests = await stubAuditExport(page, {
    body: CSV_BODY,
    contentType: 'text/csv',
    fileName: 'maran-audit-20260910T090000Z.csv',
  })

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Export' }).click()
  const downloading = page.waitForEvent('download')
  await page.getByRole('menuitem', { name: 'Export as CSV' }).click()
  const download = await downloading

  expect(download.suggestedFilename()).toBe('maran-audit-20260910T090000Z.csv')
  expect(await readFile(await download.path(), 'utf8')).toBe(CSV_BODY)
  expect(requests().at(-1)?.searchParams.get('format')).toBe('csv')
  await expect(page.getByText('Export downloaded')).toBeVisible()
})

test('the JSON command asks for JSON', async ({ page }) => {
  await openJournal(page)
  const requests = await stubAuditExport(page, {
    body: '[]',
    contentType: 'application/json',
    fileName: 'maran-audit-20260910T090000Z.json',
  })

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Export' }).click()
  const downloading = page.waitForEvent('download')
  await page.getByRole('menuitem', { name: 'Export as JSON' }).click()

  expect((await downloading).suggestedFilename()).toBe('maran-audit-20260910T090000Z.json')
  expect(requests().at(-1)?.searchParams.get('format')).toBe('json')
})

test('the export carries the search and filters applied, and neither the page nor the sort', async ({ page }) => {
  await openJournal(page)
  const requests = await stubAuditExport(page, {
    body: '[]',
    contentType: 'application/json',
    fileName: 'maran-audit-20260910T090000Z.json',
  })

  await page.goto('/settings/audit?search=mallory&outcome=failed&actor=mallory&sort=actor&pageSize=10')
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Export' }).click()
  const downloading = page.waitForEvent('download')
  await page.getByRole('menuitem', { name: 'Export as JSON' }).click()
  await downloading

  const sent = requests().at(-1)
  expect(sent?.searchParams.get('search')).toBe('mallory')
  expect(sent?.searchParams.get('outcome')).toBe('failed')
  expect(sent?.searchParams.get('actor')).toBe('mallory')
  // An export is the whole matching journal, in the panel's order: no page, size or sort is sent.
  expect(sent?.searchParams.has('page')).toBe(false)
  expect(sent?.searchParams.has('pageSize')).toBe(false)
  expect(sent?.searchParams.has('sort')).toBe(false)
})

test('a refused export shows the panel’s sentence and saves no file', async ({ page, guard }) => {
  // The refusal is the subject: the panel declines an export too large to hand over with a 400.
  guard.allow(400, /\/api\/v1\/audit\/export(\?.*)?$/)
  await openJournal(page)
  await stubAuditExportRefused(page, 'This export is too large. Narrow the window and try again.')
  let saved = false
  page.on('download', () => {
    saved = true
  })

  await page.goto('/settings/audit')
  await page.getByRole('button', { name: 'Export' }).click()
  await page.getByRole('menuitem', { name: 'Export as CSV' }).click()

  await expect(page.getByText('This export is too large. Narrow the window and try again.')).toBeVisible()
  await expect(page.getByText('Export downloaded')).toHaveCount(0)
  expect(saved).toBe(false)
})
