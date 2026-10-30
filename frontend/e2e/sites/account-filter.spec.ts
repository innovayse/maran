import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubSites, stubPhpVersions } from '../fixtures/stub-sites-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

// Issue #223: the account page links to this list as `?account=<id>`. The list then asks the panel
// for that account's rows only, says so with a removable chip, and offers the way back; a
// customer's list is already their own, so the parameter changes nothing for them.

const LICENSED: PanelModule[] = [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }]

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

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  domain: 'alpha.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const BETA: Site = {
  ...ALPHA,
  id: '33333333-3333-3333-3333-333333333333',
  accountId: BOB.id,
  ownerName: BOB.name,
  domain: 'beta.example.com',
}

/**
 * Signs in and installs the screen's stubs.
 * @param page The Playwright page.
 * @param user The signed-in user; the administrator when omitted.
 * @returns What each list read sent as `accountId`, `''` for none.
 */
const openScreen = async (page: Page, user?: AuthenticatedUser): Promise<() => readonly string[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  const requests = await stubSites(page, [ALPHA, BETA])
  await stubPhpVersions(page, [])
  return () => {
    return requests().map((url) => {
      return url.searchParams.get('accountId') ?? ''
    })
  }
}

test('a list opened for one account asks for that account and says so', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto(`/sites?account=${ALICE.id}`)

  await expect(page.getByTestId('active-filter')).toContainText('Account: alice')
  await expect(page.getByText('alpha.example.com').first()).toBeVisible()
  await expect(page.getByText('beta.example.com')).toHaveCount(0)
  expect(requested()).toContain(ALICE.id)
})

test('removing the account chip drops it from the URL and lists every row again', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto(`/sites?account=${ALICE.id}`)
  await page.getByRole('button', { name: 'Remove filter: Account: alice' }).click()

  await expect(page).not.toHaveURL(/account=/)
  await expect(page.getByTestId('active-filter')).toHaveCount(0)
  await expect(page.getByText('beta.example.com').first()).toBeVisible()
  expect(requested().at(-1)).toBe('')
})

test('a customer is unaffected by the parameter', async ({ page }) => {
  const requested = await openScreen(page, CUSTOMER)

  await page.goto(`/sites?account=${ALICE.id}`)

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByTestId('active-filter')).toHaveCount(0)
  await expect.poll(() => {return requested().length}).toBeGreaterThan(0)
  expect(requested()).not.toContain(ALICE.id)
})
