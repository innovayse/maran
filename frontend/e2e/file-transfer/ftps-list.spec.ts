import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpUsersProblem, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { FtpUser } from '../../src/types/ftpUser'

// The FTPS logins page (#196): one module's logins, one server page at a time. The page, its size,
// the search, the sort and the account filter live in the URL; the panel answers one page and the
// total, and the SPA filters nothing.

const LICENSED: PanelModule[] = [
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const BOB: Account = { ...ALICE, id: '55555555-5555-5555-5555-555555555555', name: 'bob', primaryDomain: 'bob.example.com' }

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxFtpUsers: 5,
    maxSftpUsers: 5,
    maxCronEntries: 5,
  },
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

const WEB: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'web',
  fullName: 'alice_web',
  protocol: 'Ftps',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Builds numbered FTPS logins, `alice_login01`, `bob_login02` and so on, alternating owners.
 * @param count How many logins.
 * @returns The logins, in name order.
 */
const catalogue = (count: number): FtpUser[] => {
  return Array.from({ length: count }, (_, index): FtpUser => {
    const number = String(index + 1).padStart(2, '0')
    const owner = index % 2 === 0 ? ALICE : BOB
    return {
      id: `00000000-0000-0000-0000-0000000000${number}`,
      accountId: owner.id,
      ownerName: owner.name,
      name: `login${number}`,
      fullName: `${owner.name}_login${number}`,
      protocol: 'Ftps',
      createdAt: `2026-08-${number}T10:00:00Z`,
    }
  })
}

/**
 * Signs in and serves the given FTPS logins.
 * @param page The Playwright page.
 * @param users The logins the panel reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns Every list request the page made so far.
 */
const openWith = async (page: Page, users: FtpUser[], user?: AuthenticatedUser): Promise<() => readonly URL[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  // The administrator's service card reads these two; a customer's page never asks.
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  return await stubFtpUsers(page, users)
}

/**
 * The login cells on screen, in order.
 * @param page The Playwright page.
 * @returns A locator over the table's login cells.
 */
const loginCells = (page: Page): Locator => {
  return page
    .getByRole('table', { name: 'FTPS logins' })
    .getByRole('cell')
    .filter({ hasText: /(alice|bob)_login\d\d/ })
}

test('the FTPS page shows the empty state when the panel reports no login', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/file-transfer/ftps')

  await expect(page.getByRole('heading', { level: 1, name: 'FTPS logins' })).toBeVisible()
  await expect(page.getByText('No FTPS logins yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search FTPS logins' })).toHaveCount(0)
})

// Somebody who reads `web` here and types `web` into a client cannot log in; `alice_web` is what
// the host holds in /etc/passwd, and the SPA never assembles that name itself.
test('a row shows the prefixed login and names its owning account rather than its identifier', async ({ page }) => {
  await openWith(page, [WEB])

  await page.goto('/file-transfer/ftps')

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await expect(row).toBeVisible()
  await expect(row).toContainText('alice')
  await expect(row).not.toContainText(ALICE.id)
  await expect(page.getByRole('cell', { name: 'web', exact: true })).toHaveCount(0)
  await expect(page.getByRole('columnheader', { name: 'Account' })).toBeVisible()
  await expect(page.getByText('FTPS logins are separate users from SFTP logins on this server.', { exact: false })).toBeVisible()
})

// A customer's logins are all their own, so a column naming the owner would say nothing.
test('a customer sees no account column', async ({ page }) => {
  await openWith(page, [WEB], CUSTOMER)

  await page.goto('/file-transfer/ftps')

  // Positive control: the table is drawn before a column's absence is judged.
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Login' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Account' })).toHaveCount(0)
})

test('thirty logins are paged, and the second page is asked of the panel', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/file-transfer/ftps')
  await expect(loginCells(page)).toHaveCount(25)
  await expect(loginCells(page).first()).toHaveText('alice_login01')

  await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()

  await expect(page).toHaveURL(/\/file-transfer\/ftps\?page=2$/)
  await expect(loginCells(page)).toHaveCount(5)
  await expect(loginCells(page).first()).toHaveText('bob_login26')
  expect(requests().at(-1)?.searchParams.get('page')).toBe('2')
})

test('a search reaches the URL, asks the panel, and narrows the rows', async ({ page }) => {
  const requests = await openWith(page, catalogue(30))

  await page.goto('/file-transfer/ftps')
  await page.getByRole('searchbox', { name: 'Search FTPS logins' }).fill('login1')

  await expect(page).toHaveURL(/\/file-transfer\/ftps\?search=login1$/)
  await expect(loginCells(page)).toHaveCount(10)
  expect(
    requests().some((url) => {
      return url.searchParams.get('search') === 'login1'
    }),
  ).toBe(true)
})

test('the login heading sorts on the server, ascending then descending', async ({ page }) => {
  const requests = await openWith(page, catalogue(3))

  await page.goto('/file-transfer/ftps')
  const heading = page.getByRole('columnheader', { name: 'Login' })

  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/\?sort=name$/)
  await expect(heading).toHaveAttribute('aria-sort', 'ascending')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('name')

  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/\?sort=-name$/)
  await expect(loginCells(page).first()).toHaveText('bob_login02')
  expect(requests().at(-1)?.searchParams.get('sort')).toBe('-name')
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, catalogue(3))

  await page.goto('/file-transfer/ftps?search=nowhere')

  await expect(page.getByText('No FTPS logins match')).toBeVisible()
  await expect(page.getByText('No FTPS logins yet')).toHaveCount(0)

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/file-transfer\/ftps$/)
  await expect(loginCells(page)).toHaveCount(3)
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
//
// This is also where a REFUSAL lands. When the agent refuses because the account is busy, the wire
// carries no code that tells it apart from a host failure, so the screen renders the server's own
// sentence and states no distinction of its own.
test('a failed list renders the backend RFC 7807 detail verbatim and adds no copy of its own', async ({ page }) => {
  const backendDetail = 'The account is busy with another operation. Try again in a moment.'
  await openWith(page, [])
  await stubFtpUsersProblem(page, backendDetail)

  await page.goto('/file-transfer/ftps')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No FTPS logins yet')).toHaveCount(0)
})

// Horizontal space is the scarce thing on a phone. The table is allowed to scroll — inside its own
// container — but the page behind it must not.
test('the table scrolls inside its own container rather than moving the page sideways', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 780 })
  await openWith(page, [WEB])

  await page.goto('/file-transfer/ftps')
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()

  const document = await page.evaluate(() => {
    const root = window.document.documentElement
    return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
  })
  expect(document.scrollWidth).toEqual(document.clientWidth)
})
