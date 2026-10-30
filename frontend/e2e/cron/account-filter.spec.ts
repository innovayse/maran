import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubAccountFilteredList } from '../fixtures/stub-account-filtered-list'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubCronEntries, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { CronEntry } from '../../src/types/cronEntry'
import type { PanelModule } from '../../src/types/module'

// Issue #223: the account page links to cron as `?account=<id>`. Cron always reads one account's
// crontab, so the parameter chooses the picker's account instead of the first one; a customer has
// no picker and keeps reading their own crontab whatever the URL says.

const LICENSED: PanelModule[] = [{ name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true }]

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

const NIGHTLY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: BOB.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '*' },
  command: '/usr/bin/php /home/bob/cleanup.php',
  enabled: true,
}

/**
 * Signs in and stubs the cron screen, recording which account each entries read named.
 * @param page The Playwright page.
 * @param user The signed-in user; the administrator when omitted.
 * @returns What each entries read sent as `accountId`.
 */
const openScreen = async (page: Page, user?: AuthenticatedUser): Promise<() => readonly string[]> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubCronEntries(page, [])
  await stubCronEnvironment(page, [])
  return await stubAccountFilteredList(page, '/api/v1/cron-entries', [NIGHTLY])
}

test('cron opened for one account reads that account rather than the first', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto(`/cron?account=${BOB.id}`)

  await expect(page.getByRole('row').filter({ hasText: NIGHTLY.command })).toBeVisible()
  expect(requested()).toContain(BOB.id)
  expect(requested()).not.toContain(ALICE.id)
})

test('cron without the parameter still opens on the first account', async ({ page }) => {
  const requested = await openScreen(page)

  await page.goto('/cron')

  await expect.poll(() => {return requested().length}).toBeGreaterThan(0)
  expect(requested()[0]).toBe(ALICE.id)
})

test('a customer reads their own crontab whatever the URL names', async ({ page }) => {
  const requested = await openScreen(page, CUSTOMER)

  await page.goto(`/cron?account=${BOB.id}`)

  await expect.poll(() => {return requested().length}).toBeGreaterThan(0)
  expect(requested()).not.toContain(BOB.id)
  expect(requested()).toContain(OWN_ACCOUNT.id)
})
