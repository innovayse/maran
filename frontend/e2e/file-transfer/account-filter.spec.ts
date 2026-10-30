import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubAccountFilteredList } from '../fixtures/stub-account-filtered-list'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { FtpUser } from '../../src/types/ftpUser'
import type { SftpUser } from '../../src/types/sftpUser'

// Issue #223: the account page links to this list as `?account=<id>`. The list then asks the panel
// for that account's rows only, says so, and offers the way back; a customer's list is already
// their own, so the parameter changes nothing for them.

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
    maxSftpUsers: 5,
    maxFtpUsers: 5,
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

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  name: 'web',
  fullName: 'alice_web',
  createdAt: '2026-08-01T10:00:00Z',
}

const UPLOAD: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: BOB.id,
  name: 'upload',
  fullName: 'bob_upload',
  protocol: 'Ftps',
  createdAt: '2026-08-02T10:00:00Z',
}

/**
 * Signs in, installs the screen's own stubs, then the filtered list read on top of them.
 * @param page The Playwright page.
 * @param user The signed-in user; the administrator when omitted.
 * @returns What each list read sent as `accountId`, `''` for none.
 */
const openScreen = async (page: Page, user?: AuthenticatedUser): Promise<() => readonly string[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  // Both halves of the screen are narrowed; the SFTP read is the one this spec counts.
  await stubAccountFilteredList(page, '/api/v1/ftp-users', [UPLOAD])
  return await stubAccountFilteredList(page, '/api/v1/sftp-users', [WEB])
}

test('a list opened for one account asks for that account and says so', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto(`/file-transfer?account=${ALICE.id}`)

  await expect(page.getByTestId('active-filter')).toContainText('Only alice')
  await expect(page.getByText('alice_web', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('bob_upload', { exact: true })).toHaveCount(0)
  expect(requested()).toContain(ALICE.id)
})

test('show all drops the account from the URL and lists every row again', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto(`/file-transfer?account=${ALICE.id}`)
  await page.getByRole('button', { name: 'Show all' }).click()

  await expect(page).not.toHaveURL(/account=/)
  await expect(page.getByTestId('active-filter')).toHaveCount(0)
  await expect(page.getByText('bob_upload', { exact: true }).first()).toBeVisible()
  expect(requested().at(-1)).toBe('')
})

test('a customer is unaffected by the parameter', async ({ page }) => {
  const requested = await openScreen(page, CUSTOMER)

  await page.goto(`/file-transfer?account=${ALICE.id}`)

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByTestId('active-filter')).toHaveCount(0)
  await expect.poll(() => {return requested().length}).toBeGreaterThan(0)
  expect(requested()).not.toContain(ALICE.id)
})
