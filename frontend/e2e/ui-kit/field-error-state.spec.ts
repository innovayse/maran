import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { apiKeyRow, stubApiKeys, type ApiKeyCalls } from '../fixtures/stub-api-keys-routes'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSftpUserKeys, stubSftpUsers, type SftpKeyStubReads } from '../fixtures/stub-sftp-routes'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account, MyAccount } from '../../src/types/account'
import type { SftpUser, SftpUserKey } from '../../src/types/sftpUser'
import type { Locator, Page } from '@playwright/test'

// The error state `UiDateInput` and `UiSearchInput` share with `UiInput` (issue #343): the field is
// `aria-invalid`, its `aria-describedby` names the message, and the message is on screen. Both are
// reached on the screens that use them, by clicks alone:
//   - the date field of the create-an-API-key dialog, refused while the chosen day is already over;
//   - the search field of an SFTP login's keys dialog, refused while the term is longer than a list
//     accepts (the backend's PageDefaults.SearchMaxLength, 100).
// Each refusing case has an accepting control, because a field that always claimed an error would
// pass every test that only hands it bad input.

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

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'web',
  fullName: 'alice_web',
  protocol: 'Sftp',
  createdAt: '2026-08-01T10:00:00Z',
}

const LAPTOP: SftpUserKey = {
  id: 'bbbbbbbb-0000-0000-0000-000000000001',
  label: 'Work laptop',
  algorithm: 'ssh-ed25519',
  fingerprint: 'SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8',
  createdAt: '2026-08-10T10:00:00Z',
}

/** The longest term a list accepts, and one character more. */
const LONGEST_TERM = 'a'.repeat(100)
const TOO_LONG_TERM = 'a'.repeat(101)

/** The sentences under test, in English. */
const EXPIRY_PAST = 'Choose a day in the future.'
const SEARCH_TOO_LONG = 'The search is too long. Use at most 100 characters.'

/**
 * Signs in as an administrator with every module licensed and answers the reads of the shell and of
 * the landing dashboard, so the shell around the screen is drawn and nothing else is asked for.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const openShell = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubShellReads(page)
  await stubLandingReads(page)
}

/**
 * Opens the create-an-API-key dialog by clicks: the Security group, API keys, then Create key.
 * @param page The Playwright page.
 * @returns The dialog and the calls the stub records.
 */
const openCreateKeyDialog = async (page: Page): Promise<{ dialog: Locator; calls: ApiKeyCalls }> => {
  await openShell(page)
  const calls = await stubApiKeys(page, { keys: [apiKeyRow()] })
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  await openSection(page, 'Security')
  await mainNavigation(page).getByRole('link', { name: 'API keys', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'API keys' })).toBeVisible()
  await page.getByRole('button', { name: 'Create key' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create an API key' })
  await expect(dialog).toBeVisible()
  return { dialog, calls }
}

/**
 * Opens the keys dialog of `alice_web` by clicks: Files and access, SFTP logins, the row's menu, Keys.
 * @param page The Playwright page.
 * @returns The dialog and a reader of the key-list requests the page made.
 */
const openKeysDialog = async (page: Page): Promise<{ dialog: Locator; reads: SftpKeyStubReads }> => {
  await openShell(page)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubSftpUsers(page, [WEB])
  const reads = await stubSftpUserKeys(page, [LAPTOP])
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  await openSection(page, 'Files and access')
  await mainNavigation(page).getByRole('link', { name: 'SFTP logins', exact: true }).click()
  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await row.getByRole('button', { name: 'Actions for alice_web' }).click()
  // The menu is rendered into `body`, so it is queried from the page rather than the row.
  await page.getByRole('menuitem', { name: 'Keys', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'SSH keys for alice_web' })
  await expect(dialog).toBeVisible()
  return { dialog, reads }
}

/**
 * Asserts the field is in the error state: invalid, described by the message, and the message shown.
 * @param field The input.
 * @param message The sentence the field must show.
 * @returns Resolves once all three hold.
 */
const expectErrorState = async (field: Locator, message: string): Promise<void> => {
  await expect(field).toHaveAttribute('aria-invalid', 'true')
  const describedBy = await field.getAttribute('aria-describedby')
  expect(describedBy).not.toBeNull()
  const messageElement = field.page().locator(`[id="${describedBy}"]`)
  await expect(messageElement).toHaveText(message)
  await expect(messageElement).toBeVisible()
  await expect(field).toHaveAccessibleDescription(message)
}

/**
 * Asserts the field is not in the error state: not invalid, described by nothing, no message shown.
 * @param field The input.
 * @param message The sentence that must not be on screen.
 * @returns Resolves once all three hold.
 */
const expectNoErrorState = async (field: Locator, message: string): Promise<void> => {
  await expect(field).toHaveAttribute('aria-invalid', 'false')
  await expect(field).not.toHaveAttribute('aria-describedby', /.+/)
  await expect(field.page().getByText(message)).toHaveCount(0)
}

test('a date field refuses a day already over: invalid, described by its message, which is visible', async ({
  page,
}) => {
  const { dialog, calls } = await openCreateKeyDialog(page)
  const expires = dialog.getByLabel('Expires on')
  await dialog.getByRole('textbox', { name: 'Name' }).fill('Nightly export')
  await expires.fill('2020-01-01')

  // The rule shows once the form was submitted, so before it the field is still quiet.
  await expectNoErrorState(expires, EXPIRY_PAST)

  await dialog.getByRole('button', { name: 'Create key' }).click()

  await expectErrorState(expires, EXPIRY_PAST)
  expect(calls.created).toHaveLength(0)
})

test('a date field with a day in the future is valid and the form goes through', async ({ page }) => {
  const { dialog, calls } = await openCreateKeyDialog(page)
  const expires = dialog.getByLabel('Expires on')
  await dialog.getByRole('textbox', { name: 'Name' }).fill('Nightly export')
  await expires.fill('2099-12-31')

  await dialog.getByRole('button', { name: 'Create key' }).click()

  await expect(page.getByRole('dialog', { name: 'Your new API key' })).toBeVisible()
  expect(calls.created).toHaveLength(1)
})

test('a search field refuses a term longer than a list accepts and sends nothing until it is shortened', async ({
  page,
}) => {
  const { dialog, reads } = await openKeysDialog(page)
  const search = dialog.getByRole('searchbox', { name: 'Search keys' })
  await expectNoErrorState(search, SEARCH_TOO_LONG)

  await search.fill(TOO_LONG_TERM)

  await expectErrorState(search, SEARCH_TOO_LONG)

  // Shortening the term clears the error and sends the short one. The short term arriving after the
  // debounce is what proves the long one was never sent: had it been, it would have gone first.
  await search.fill('laptop')
  await expectNoErrorState(search, SEARCH_TOO_LONG)
  await expect
    .poll(() => {
      return reads.listRequests().at(-1)?.searchParams.get('search')
    })
    .toBe('laptop')
  const sent = reads.listRequests().map((url) => {
    return url.searchParams.get('search')
  })
  expect(sent).not.toContain(TOO_LONG_TERM)
})

test('a search field accepts a term of exactly the longest length and sends it', async ({ page }) => {
  const { dialog, reads } = await openKeysDialog(page)
  const search = dialog.getByRole('searchbox', { name: 'Search keys' })

  await search.fill(LONGEST_TERM)

  await expectNoErrorState(search, SEARCH_TOO_LONG)
  await expect
    .poll(() => {
      return reads.listRequests().at(-1)?.searchParams.get('search')
    })
    .toBe(LONGEST_TERM)
})
