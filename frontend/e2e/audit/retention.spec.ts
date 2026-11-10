import { expect, test } from '../fixtures/guarded-test'
import { AUDIT_RETENTION_PATTERN, DEFAULT_RETENTION, stubAudit, stubAuditRetention } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuditRetention } from '../../src/types/audit'
import type { Page } from '@playwright/test'

// The journal's retention (#145): how long entries are kept. The bounds and the rule are the
// panel's; the card shows the setting, sends a day count and renders the panel's refusals.

/**
 * Signs an administrator in over an empty journal and installs the retention the panel holds.
 * @param page The Playwright page.
 * @param retention The setting the panel holds before any save.
 * @returns Every day count a save sent so far.
 */
const openJournal = async (page: Page, retention: AuditRetention): Promise<() => readonly (number | null)[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubAudit(page, [])
  return await stubAuditRetention(page, retention)
}

test('the card starts on keep forever when the panel holds no limit', async ({ page }) => {
  await openJournal(page, DEFAULT_RETENTION)

  await page.goto('/settings/audit')

  await expect(page.getByRole('switch', { name: 'Keep forever' })).toBeChecked()
  await expect(page.getByRole('textbox', { name: 'Keep for (days)' })).toBeDisabled()
})

test('a limit the panel holds is shown with its last run', async ({ page }) => {
  await openJournal(page, { ...DEFAULT_RETENTION, retentionDays: 365, lastRunAt: '2026-09-10T12:00:00+00:00' })

  await page.goto('/settings/audit')

  await expect(page.getByRole('switch', { name: 'Keep forever' })).not.toBeChecked()
  await expect(page.getByRole('textbox', { name: 'Keep for (days)' })).toHaveValue('365')
  await expect(page.getByText('10 Sep 2026')).toBeVisible()
})

test('turning keep forever off and saving sends the day count and shows what the panel stored', async ({ page }) => {
  const saved = await openJournal(page, DEFAULT_RETENTION)

  await page.goto('/settings/audit')
  await page.getByRole('switch', { name: 'Keep forever' }).click()
  await page.getByRole('textbox', { name: 'Keep for (days)' }).fill('90')
  await page.getByRole('button', { name: 'Save retention' }).click()

  await expect(page.getByText('Retention saved')).toBeVisible()
  expect(saved()).toEqual([90])
  await expect(page.getByRole('textbox', { name: 'Keep for (days)' })).toHaveValue('90')
})

test('switching a limit back to keep forever sends no day count', async ({ page }) => {
  const saved = await openJournal(page, { ...DEFAULT_RETENTION, retentionDays: 365 })

  await page.goto('/settings/audit')
  await page.getByRole('switch', { name: 'Keep forever' }).click()
  await page.getByRole('button', { name: 'Save retention' }).click()

  await expect(page.getByText('Retention saved')).toBeVisible()
  expect(saved()).toEqual([null])
})

test('a day count outside the panel’s bounds is refused in the panel’s words', async ({ page, guard }) => {
  // The refusal is the subject: the panel answers a day count outside its bounds with a 400.
  guard.allow(400, AUDIT_RETENTION_PATTERN)
  const saved = await openJournal(page, DEFAULT_RETENTION)

  await page.goto('/settings/audit')
  await page.getByRole('switch', { name: 'Keep forever' }).click()
  await page.getByRole('textbox', { name: 'Keep for (days)' }).fill('5')
  await page.getByRole('button', { name: 'Save retention' }).click()

  await expect(page.getByText('Keep entries for between 30 and 3650 days.')).toBeVisible()
  await expect(page.getByText('Retention saved')).toHaveCount(0)
  // The refusal is the panel's: the card sent the value instead of judging it.
  expect(saved()).toEqual([5])
})

test('text that is not a whole number cannot be saved, so it never reaches the panel as keep forever', async ({
  page,
}) => {
  const saved = await openJournal(page, DEFAULT_RETENTION)

  await page.goto('/settings/audit')
  await page.getByRole('switch', { name: 'Keep forever' }).click()
  await page.getByRole('textbox', { name: 'Keep for (days)' }).fill('soon')

  await expect(page.getByRole('button', { name: 'Save retention' })).toBeDisabled()
  expect(saved()).toEqual([])
})
