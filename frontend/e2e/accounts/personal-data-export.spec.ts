import { readFile } from 'node:fs/promises'
import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { ACME_HOSTING } from '../fixtures/stub-resellers-routes'
import type { Account } from '../../src/types/account'
import type { Page } from '@playwright/test'

// The personal-data export of one account (#146): an administrator downloads everything the panel
// holds about the account and its team from the account's page. What the file holds is the panel's;
// these specs cover the button, the download and the refusal.

/** The account under test. */
const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
}

/** The document the stubbed panel streams; the spec reads it back from the saved file. */
const EXPORT_BODY = '{"subject":{"kind":"account"},"sections":[],"auditEntries":[]}'

/**
 * Answers the account's detail read.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the route is installed.
 */
const stubAccount = async (page: Page): Promise<void> => {
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
  })
}

/**
 * Answers the account's resource summary, which every role's account page reads.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
const stubSummary = async (page: Page): Promise<void> => {
  await stubAccountResources(page, { accountId: ACCOUNT.id, resources: [] })
}

/**
 * Signs an administrator in and installs the account's page reads.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the stubs are installed.
 */
const openAsAdministrator = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubShellReads(page)
  await stubSummary(page)
  await stubInvitation(page, ACCOUNT.id, 'pending')
  await stubAccount(page)
}

test('the administrator downloads the account’s personal data from its page', async ({ page }) => {
  await openAsAdministrator(page)
  let asked = 0
  await page.route(`**/api/v1/privacy/accounts/${ACCOUNT.id}/export`, async (route) => {
    asked += 1
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: { 'Content-Disposition': 'attachment; filename="maran-personal-data-acme.json"' },
      body: EXPORT_BODY,
    })
  })

  await page.goto(`/accounts/${ACCOUNT.id}`)
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export personal data' }).click()
  const download = await downloading

  expect(download.suggestedFilename()).toBe('maran-personal-data-acme.json')
  expect(await readFile(await download.path(), 'utf8')).toBe(EXPORT_BODY)
  expect(asked).toBe(1)
  await expect(page.getByText('Done', { exact: true })).toBeVisible()
})

test('a refused export shows the panel’s sentence and saves no file', async ({ page, guard }) => {
  // The refusal is the subject: the export answers 404 with the panel's own sentence.
  guard.allow(404, new RegExp(`/api/v1/privacy/accounts/${ACCOUNT.id}/export$`))
  await openAsAdministrator(page)
  await page.route(`**/api/v1/privacy/accounts/${ACCOUNT.id}/export`, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'PrivacySubjectNotFound', title: 'Not found', detail: 'No such account.' }),
    })
  })
  let saved = false
  page.on('download', () => {
    saved = true
  })

  await page.goto(`/accounts/${ACCOUNT.id}`)
  await page.getByRole('button', { name: 'Export personal data' }).click()

  await expect(page.getByText('No such account.')).toBeVisible()
  expect(saved).toBe(false)
})

test('a reseller opening the account is not offered the export', async ({ page }) => {
  await stubResellerSession(page, ACME_HOSTING, RESELLER_MODULES)
  await stubSummary(page)
  await stubAccount(page)

  await page.goto(`/accounts/${ACCOUNT.id}`)

  // The page itself drew (its own action is there), so the missing card is the role gate's doing.
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Export personal data' })).toHaveCount(0)
})
