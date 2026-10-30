import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { FtpUser } from '../../src/types/ftpUser'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

// Issue #223: the account page links to each list as `?account=<id>`. The list then asks the panel
// for that account's rows only, says so with a removable chip, and offers the way back; a
// customer's list is already their own, so the parameter changes nothing for them. Both pages, one
// per module, behave alike.

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

const ALICE_SFTP: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'web',
  fullName: 'alice_web',
  protocol: 'Sftp',
  createdAt: '2026-08-01T10:00:00Z',
}

const BOB_SFTP: SftpUser = {
  ...ALICE_SFTP,
  id: '12121212-1212-1212-1212-121212121212',
  accountId: BOB.id,
  ownerName: BOB.name,
  fullName: 'bob_web',
}

const ALICE_FTP: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'upload',
  fullName: 'alice_upload',
  protocol: 'Ftps',
  createdAt: '2026-08-02T10:00:00Z',
}

const BOB_FTP: FtpUser = {
  ...ALICE_FTP,
  id: '34343434-3434-3434-3434-343434343434',
  accountId: BOB.id,
  ownerName: BOB.name,
  fullName: 'bob_upload',
}

/** One page under test: where it lives and the two rows it serves. */
interface Screen {
  /** The page's address. */
  path: string
  /** Alice's login on it. */
  aliceLogin: string
  /** Bob's login on it. */
  bobLogin: string
  /** Which endpoint's requests the spec reads. */
  endpoint: 'sftp' | 'ftp'
}

const SCREENS: Screen[] = [
  { path: '/file-transfer/sftp', aliceLogin: 'alice_web', bobLogin: 'bob_web', endpoint: 'sftp' },
  { path: '/file-transfer/ftps', aliceLogin: 'alice_upload', bobLogin: 'bob_upload', endpoint: 'ftp' },
]

/**
 * Signs in and installs both pages' stubs.
 * @param page The Playwright page.
 * @param endpoint Which endpoint's list requests to report.
 * @param user The signed-in user; the administrator when omitted.
 * @returns What each list read of that endpoint sent as `accountId`, `''` for none.
 */
const openScreen = async (
  page: Page,
  endpoint: Screen['endpoint'],
  user?: AuthenticatedUser,
): Promise<() => readonly string[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  const sftp = await stubSftpUsers(page, [ALICE_SFTP, BOB_SFTP])
  const ftp = await stubFtpUsers(page, [ALICE_FTP, BOB_FTP])
  const requests = endpoint === 'sftp' ? sftp : ftp
  return () => {
    return requests().map((url) => {
      return url.searchParams.get('accountId') ?? ''
    })
  }
}

for (const screen of SCREENS) {
  test(`${screen.path}: a list opened for one account asks for that account and says so`, async ({ page }) => {
    const requested = await openScreen(page, screen.endpoint)

    await page.goto(`${screen.path}?account=${ALICE.id}`)

    await expect(page.getByTestId('active-filter')).toContainText('Account: alice')
    await expect(page.getByText(screen.aliceLogin, { exact: true }).first()).toBeVisible()
    await expect(page.getByText(screen.bobLogin, { exact: true })).toHaveCount(0)
    expect(requested()).toContain(ALICE.id)
  })

  test(`${screen.path}: removing the account chip drops it from the URL and lists every row again`, async ({
    page,
  }) => {
    const requested = await openScreen(page, screen.endpoint)

    await page.goto(`${screen.path}?account=${ALICE.id}`)
    await page.getByRole('button', { name: 'Remove filter: Account: alice' }).click()

    await expect(page).not.toHaveURL(/account=/)
    await expect(page.getByTestId('active-filter')).toHaveCount(0)
    await expect(page.getByText(screen.bobLogin, { exact: true }).first()).toBeVisible()
    expect(requested().at(-1)).toBe('')
  })

  test(`${screen.path}: the filter sheet narrows the list to the chosen account`, async ({ page }) => {
    const requested = await openScreen(page, screen.endpoint)

    await page.goto(screen.path)
    await page.getByTestId('filter-trigger').click()
    const dialog = page.getByRole('dialog', { name: 'Filters' })
    await dialog.getByRole('combobox', { name: 'Account' }).click()
    await page.getByRole('option', { name: 'bob' }).click()
    await dialog.getByRole('button', { name: 'Apply' }).click()

    await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
    await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')
    await expect(page.getByText(screen.bobLogin, { exact: true }).first()).toBeVisible()
    await expect(page.getByText(screen.aliceLogin, { exact: true })).toHaveCount(0)
    expect(requested()).toContain(BOB.id)
  })

  test(`${screen.path}: a customer is unaffected by the parameter and offered no filter`, async ({ page }) => {
    const requested = await openScreen(page, screen.endpoint, CUSTOMER)

    await page.goto(`${screen.path}?account=${ALICE.id}`)

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect
      .poll(() => {
        return requested().length
      })
      .toBeGreaterThan(0)
    await expect(page.getByTestId('active-filter')).toHaveCount(0)
    await expect(page.getByTestId('filter-trigger')).toHaveCount(0)
    expect(requested()).not.toContain(ALICE.id)
  })
}
