import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases, stubDatabasesProblem } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'sftp', displayName: 'SFTP', tier: 'included', isEnabled: true },
  // A module this bundle has no glyph for, so a spec can tell a chosen icon from the neutral one.
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const SHOP: Database = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  name: 'shop',
  fullName: 'alice_shop',
  dbUserName: 'alice_shopuser',
  createdAt: '2026-08-01T10:00:00Z',
}

test('the databases screen shows the empty state when the panel reports no databases', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/databases')

  await expect(page.getByText('No databases yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

// The whole point of the column. An operator who reads `shop` here and types `shop` into a mysql
// client is told the database does not exist; `alice_shop` is what MySQL holds.
test('the panel shows the prefixed name so the operator can find it in mysql', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])

  await page.goto('/databases')

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  await expect(row).toContainText('alice_shop')
  await expect(row).toContainText('alice_shopuser')
  // The bare suffix is not offered anywhere as a name in its own right, so there is nothing to
  // copy by mistake.
  await expect(page.getByRole('cell', { name: 'shop', exact: true })).toHaveCount(0)
  // And the screen says whose prefix it is, rather than leaving the reader to infer it.
  await expect(
    page.getByText("Every database and user carries the owning account's name as a prefix.", {
      exact: false,
    }),
  ).toBeVisible()
})

test('the row names the account that owns the database rather than printing its identifier', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])

  await page.goto('/databases')

  const row = page.getByRole('row').filter({ hasText: 'alice_shop' })
  await expect(row).toContainText('alice')
  await expect(row).not.toContainText(ALICE.id)
})

// The tenant boundary itself is the server's: `DatabasesDbContext`'s query filter scopes every read
// to the caller's account, and another customer's row never reaches the wire. What this spec can
// check is the half that lives here — that the page renders the answer it was given and invents no
// row of its own. It would NOT catch a broken filter on the server; the backend's own IDOR tests do.
test('a database another tenant owns is not listed', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])

  await page.goto('/databases')

  await expect(page.getByRole('row').filter({ hasText: 'alice_shop' })).toBeVisible()
  await expect(page.getByText('bob_secrets')).toHaveCount(0)
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
test('the databases screen renders the backend RFC 7807 detail verbatim when the list request fails', async ({
  page,
}) => {
  const backendDetail = 'The database service is temporarily unavailable. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabasesProblem(page, backendDetail)

  await page.goto('/databases')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No databases yet')).toHaveCount(0)
})

// Horizontal space is the scarce thing on a phone. The table is allowed to scroll — inside its own
// container — but the page behind it must not, or every screen drifts sideways under the reader.
test('the databases table scrolls inside its own container rather than moving the page sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 780 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [SHOP])

  await page.goto('/databases')
  await expect(page.getByRole('row').filter({ hasText: 'alice_shop' })).toBeVisible()

  const document = await page.evaluate(() => {
    const root = window.document.documentElement
    return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
  })
  expect(document.scrollWidth).toEqual(document.clientWidth)
})

test('the sidebar links to the databases screen when the panel licenses the module', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'Databases' }).click()

  await expect(page).toHaveURL(/\/databases$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Databases' })).toBeVisible()
})

// Three identical glyphs in a column of three rows tell the reader nothing the labels do not.
// `backups` is in the catalogue precisely as the neutral case to compare against.
test('the databases and sftp entries draw their own glyph rather than the neutral one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/')

  const navigation = page.getByRole('navigation')
  const databasesGlyph = await navigation.getByRole('link', { name: 'Databases' }).locator('svg').innerHTML()
  const sftpGlyph = await navigation.getByRole('link', { name: 'SFTP' }).locator('svg').innerHTML()
  const neutralGlyph = await navigation.getByRole('link', { name: 'Backups' }).locator('svg').innerHTML()

  expect(databasesGlyph).not.toEqual(neutralGlyph)
  expect(sftpGlyph).not.toEqual(neutralGlyph)
  expect(databasesGlyph).not.toEqual(sftpGlyph)
})
