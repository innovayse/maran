import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases, stubDatabasesProblem } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

// The databases list is server-paged (#196): the page, its size, the search, the sort and the
// account filter live in the URL, the panel answers one page, the total and each row's owner, and
// the SPA filters nothing.

const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  // A module this bundle has no glyph for, so a spec can tell a chosen icon from the neutral one.
  // It was `backups` until the backups screen landed and gave that module a glyph of its own — a
  // control has to be a module this bundle really does not draw, or the comparison compares two
  // chosen glyphs and passes for the wrong reason.
  { name: 'notifications', displayName: 'Notifications', tier: 'included', isEnabled: true },
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
  ownerName: ALICE.name,
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
// `notifications` is in the catalogue precisely as the neutral case to compare against.
test('the databases and sftp entries draw their own glyph rather than the neutral one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])

  await page.goto('/')

  const navigation = page.getByRole('navigation')
  const databasesGlyph = await navigation.getByRole('link', { name: 'Databases' }).locator('svg').innerHTML()
  const sftpGlyph = await navigation
    .getByRole('link', { name: 'File transfer', exact: true })
    .locator('svg')
    .innerHTML()
  const neutralGlyph = await navigation
    .getByRole('link', { name: 'Outgoing mail' })
    .locator('svg')
    .innerHTML()

  expect(databasesGlyph).not.toEqual(neutralGlyph)
  expect(sftpGlyph).not.toEqual(neutralGlyph)
  expect(databasesGlyph).not.toEqual(sftpGlyph)
})

const BOB: Account = { ...ALICE, id: '55555555-5555-5555-5555-555555555555', name: 'bob', primaryDomain: 'bob.example.com' }

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'alice',
  email: 'alice@example.com',
  role: 'customer',
  accountId: ALICE.id,
}

/**
 * Builds a catalogue of numbered databases: `alice_db01`, `bob_db02` and so on, alternating between
 * the two accounts, each created a day after the one before.
 * @param count How many databases.
 * @returns The databases, oldest first.
 */
const catalogue = (count: number): Database[] => {
  return Array.from({ length: count }, (_, index): Database => {
    const number = String(index + 1).padStart(2, '0')
    const owner = index % 2 === 0 ? ALICE : BOB
    return {
      id: `00000000-0000-0000-0000-0000000000${number}`,
      accountId: owner.id,
      ownerName: owner.name,
      name: `db${number}`,
      fullName: `${owner.name}_db${number}`,
      dbUserName: `${owner.name}_user${number}`,
      createdAt: `2026-08-${number}T10:00:00Z`,
    }
  })
}

/**
 * Signs in and serves the given databases and two accounts.
 * @param page The Playwright page.
 * @param databases The databases the panel reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, databases: Database[], user?: AuthenticatedUser): Promise<() => readonly URL[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  return await stubDatabases(page, databases)
}

/**
 * The full-name cells on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's first cell of each body row.
 */
const nameCells = (page: Page): Locator => {
  return page.getByRole('table').getByRole('row').filter({ has: page.getByRole('cell') }).locator('td:first-child')
}

test('a search reaches the URL, asks the panel, and survives a reload', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/databases')
  await page.getByRole('searchbox', { name: 'Search databases' }).fill('db1')

  await expect(page).toHaveURL(/\/databases\?search=db1$/)
  await expect(nameCells(page)).toHaveCount(10)
  expect(
    requests().some((url) => {
      return url.searchParams.get('search') === 'db1'
    }),
  ).toBe(true)

  await page.reload()

  await expect(page.getByRole('searchbox', { name: 'Search databases' })).toHaveValue('db1')
  await expect(nameCells(page).first()).toHaveText('bob_db10')
})

test('the next and last pages are reachable, and a new page size goes back to the first', async ({ page }) => {
  await openWith(page, catalogue(60))

  await page.goto('/databases')
  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page).toHaveURL(/\?page=2$/)
  await expect(nameCells(page).first()).toHaveText('bob_db26')

  await nav.getByRole('button', { name: 'Last page' }).click()
  await expect(page).toHaveURL(/\?page=3$/)
  await expect(nameCells(page)).toHaveCount(10)

  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '10', exact: true }).click()
  await expect(page).toHaveURL(/\?pageSize=10$/)
  await expect(nameCells(page).first()).toHaveText('alice_db01')
})

test('a page past the end moves to the last page that has rows', async ({ page }) => {
  await openWith(page, catalogue(30))

  await page.goto('/databases?page=5')

  await expect(page).toHaveURL(/\?page=2$/)
  await expect(nameCells(page)).toHaveCount(5)
})

test('the name heading sorts the list on the server by the full name', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/databases?sort=name')
  const heading = page.getByRole('columnheader', { name: 'Database' })
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  await heading.getByRole('button').click()

  await expect(page).toHaveURL(/\?sort=-name$/)
  await expect(nameCells(page)).toHaveText(['bob_db02', 'alice_db03', 'alice_db01'])
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('-name')
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/databases?search=nowhere')

  await expect(page.getByText('No databases match')).toBeVisible()
  await expect(page.getByText('No databases yet')).toHaveCount(0)

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/databases$/)
  await expect(nameCells(page)).toHaveCount(3)
})

test('the account filter applied in the sheet becomes a removable chip', async ({ page }) => {
  const requests = await openWith(page, catalogue(6))

  await page.goto('/databases')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'bob' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
  expect(
    requests().some((url) => {
      return url.searchParams.get('accountId') === BOB.id
    }),
  ).toBe(true)
  await expect(nameCells(page)).toHaveText(['bob_db02', 'bob_db04', 'bob_db06'])
  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')

  await page.getByRole('button', { name: 'Remove filter: Account: bob' }).click()
  await expect(page).toHaveURL(/\/databases$/)
  await expect(nameCells(page)).toHaveCount(6)
})

// A customer's list is already their own account's, and the account is the list's only filter:
// there is nothing to narrow, so the trigger is not offered. The server scopes the rows either way.
test('a customer is offered the search but no filter trigger', async ({ page }) => {
  await openWith(page, [SHOP], CUSTOMER)

  await page.goto('/databases')

  await expect(page.getByRole('searchbox', { name: 'Search databases' })).toBeVisible()
  await expect(page.getByTestId('filter-trigger')).toHaveCount(0)
})
