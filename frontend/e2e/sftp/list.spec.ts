import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers, stubSftpUsersProblem } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

const LICENSED: PanelModule[] = [{ name: 'sftp', displayName: 'SFTP', tier: 'included', isEnabled: true }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  name: 'web',
  fullName: 'alice_web',
  createdAt: '2026-08-01T10:00:00Z',
}

test('the sftp screen shows the empty state when the panel reports no logins', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [])

  await page.goto('/sftp-users')

  await expect(page.getByText('No SFTP logins yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

// The whole point of the column. Somebody who reads `web` here and types `web` into an SFTP client
// cannot log in; `alice_web` is what the host holds in /etc/passwd.
test('the panel shows the prefixed login so the operator can sign in with it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [WEB])

  await page.goto('/sftp-users')

  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  // The bare suffix is not offered anywhere as a login in its own right.
  await expect(page.getByRole('cell', { name: 'web', exact: true })).toHaveCount(0)
  await expect(
    page.getByText("Every login carries the owning account's name as a prefix.", { exact: false }),
  ).toBeVisible()
})

test('the row names the account that owns the login rather than printing its identifier', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [WEB])

  await page.goto('/sftp-users')

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await expect(row).toContainText('alice')
  await expect(row).not.toContainText(ALICE.id)
})

// The tenant boundary itself is the server's: `SftpDbContext`'s query filter scopes every read to
// the caller's account, and another customer's row never reaches the wire. What this spec can check
// is the half that lives here — that the page renders the answer it was given and invents no row of
// its own. It would NOT catch a broken filter on the server; the backend's own IDOR tests do.
test('an sftp login another tenant owns is not listed', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [WEB])

  await page.goto('/sftp-users')

  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(page.getByText('bob_deploy')).toHaveCount(0)
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('the sftp screen renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  const backendDetail = 'The SFTP service is temporarily unavailable. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsersProblem(page, backendDetail)

  await page.goto('/sftp-users')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No SFTP logins yet')).toHaveCount(0)
})

// Horizontal space is the scarce thing on a phone. The table is allowed to scroll — inside its own
// container — but the page behind it must not, or every screen drifts sideways under the reader.
test('the sftp table scrolls inside its own container rather than moving the page sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 780 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [WEB])

  await page.goto('/sftp-users')
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()

  const document = await page.evaluate(() => {
    const root = window.document.documentElement
    return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
  })
  expect(document.scrollWidth).toEqual(document.clientWidth)
})

test('the sidebar links to the sftp screen when the panel licenses the module', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'SFTP' }).click()

  await expect(page).toHaveURL(/\/sftp-users$/)
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP' })).toBeVisible()
})
