import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

// The "Files and access" sidebar section (#195, regrouped by #311) holds one page per module (#196):
// it lands on the SFTP logins, and with no cron module licensed its submenu holds exactly the SFTP
// and the FTPS pages. Every address the single merged screen ever had forwards to the SFTP page
// rather than turning into a 404.

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

/**
 * Signs in with both transfer modules licensed and no logins on either.
 * @param page The Playwright page.
 * @param user The signed-in user; the administrator when omitted.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page, user?: AuthenticatedUser): Promise<void> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
}

test('the Files and access entry opens the SFTP logins page', async ({ page }) => {
  await signIn(page)

  await page.goto('/')
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Files and access', exact: true })
    .click()

  await expect(page).toHaveURL(/\/file-transfer\/sftp$/)
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
  await expect(page).toHaveTitle('SFTP logins · Maran')
})

test('the submenu holds exactly the SFTP and the FTPS pages, and each leads to its own', async ({ page }) => {
  await signIn(page)

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const toggle = nav.getByRole('button', { name: 'Show the Files and access pages' })
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')

  // The section's links are the entry and its children; nothing else in the sidebar leads here.
  const hrefs = await nav.locator('a[href^="/file-transfer"]').evaluateAll((links) => {
    return links.map((link) => {
      return link.getAttribute('href') ?? ''
    })
  })
  expect([...hrefs].sort()).toEqual(['/file-transfer/ftps', '/file-transfer/sftp', '/file-transfer/sftp'])

  await nav.getByRole('link', { name: 'FTPS logins', exact: true }).click()
  await expect(page).toHaveURL(/\/file-transfer\/ftps$/)
  await expect(page.getByRole('heading', { level: 1, name: 'FTPS logins' })).toBeVisible()
  await expect(page).toHaveTitle('FTPS logins · Maran')
  await expect(nav.getByRole('link', { name: 'FTPS logins', exact: true })).toHaveAttribute('aria-current', 'page')

  await nav.getByRole('link', { name: 'SFTP logins', exact: true }).click()
  await expect(page).toHaveURL(/\/file-transfer\/sftp$/)
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
  await expect(page).toHaveTitle('SFTP logins · Maran')
})

// The FTPS module contributes no top-level entry: its page lives under Files and access. Two entries
// leading to one subject is the defect this asserts against.
test('the licensed ftp module adds no top-level sidebar entry of its own', async ({ page }) => {
  await signIn(page)

  await page.goto('/')

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  // Positive control: the sidebar has rendered the catalogue before an absence is judged.
  await expect(nav.getByRole('link', { name: 'Files and access', exact: true })).toHaveCount(1)
  await expect(nav.getByRole('link', { name: 'FTPS', exact: true })).toHaveCount(0)
})

// Neither page is the administrator's: a customer manages their own logins of both kinds.
test('a customer sees both pages in the submenu and reaches the FTPS one', async ({ page }) => {
  await signIn(page, CUSTOMER)

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Files and access pages' }).click()

  await expect(nav.getByRole('link', { name: 'SFTP logins', exact: true })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'FTPS logins', exact: true })).toBeVisible()

  await nav.getByRole('link', { name: 'FTPS logins', exact: true }).click()
  await expect(page).toHaveURL(/\/file-transfer\/ftps$/)
  await expect(page.getByRole('heading', { level: 1, name: 'FTPS logins' })).toBeVisible()
})

// Every link the panel rendered before the split points at the merged screen's address, and a
// customer's bookmark at the screen before that one. Both forward rather than turning into a 404.
for (const oldPath of ['/file-transfer', '/sftp-users']) {
  test(`the old ${oldPath} address forwards to the SFTP logins page`, async ({ page }) => {
    await signIn(page)

    await page.goto(oldPath)

    await expect(page).toHaveURL(/\/file-transfer\/sftp$/)
    await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
  })
}

test('the old address keeps an account filter across the forward', async ({ page }) => {
  await signIn(page)

  await page.goto(`/file-transfer?account=${ALICE.id}`)

  await expect(page).toHaveURL(new RegExp(`/file-transfer/sftp\\?account=${ALICE.id}$`))
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
})

// A child page whose module is disabled is locked rather than hidden: the FTPS logins child stays in
// the submenu and leads to the ftp module's upgrade page, while the entry itself still lands on the
// SFTP logins.
test('with the ftp module disabled the FTPS logins child is locked and the entry still opens the SFTP page', async ({
  page,
}) => {
  await signIn(page)
  // Registered after the catalogue `signIn` installed, so this one answers.
  await stubModules(page, [
    { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
    { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: false },
  ])

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const entry = nav.getByRole('link', { name: 'Files and access', exact: true })

  // Positive control: the entry is drawn before its submenu is judged.
  await expect(entry).toHaveCount(1)
  await nav.getByRole('button', { name: 'Show the Files and access pages' }).click()
  const ftps = nav.getByRole('link', { name: /^FTPS logins/ })
  await expect(ftps).toHaveCount(1)
  await expect(ftps).toContainText('Locked')
  await expect(ftps).toHaveAttribute('href', '/upgrade/ftp')

  await entry.click()
  await expect(page).toHaveURL(/\/file-transfer\/sftp$/)
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
  // The locked child never opens the FTPS page in its place.
  await expect(nav.getByRole('link', { name: 'FTPS logins', exact: true })).toHaveCount(0)
})
