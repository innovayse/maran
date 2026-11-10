import { expect, test } from '../fixtures/guarded-test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubMailChoices, stubMailDomains } from '../fixtures/stub-mail-routes'
import {
  MAIL_OFF,
  MAIL_ON,
  MAIL_STARTING_VALUES,
  stubMailServer,
  type MailServerStub,
} from '../fixtures/stub-mail-server-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { MailServerStatus } from '../../src/types/mailServer'
import type { Page } from '@playwright/test'

// Issue #505: with mail off, the screens told the ADMINISTRATOR to ask the server administrator, and
// no screen could turn mail on. An administrator is now told up front, with the way to switch it on,
// and the form that would only be refused is not offered. Everyone else keeps the form (and the ask
// sentence the panel answers with), and a server with mail on keeps it too.

const LICENSED: PanelModule[] = [{ name: 'mail', displayName: 'Mail', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

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

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/**
 * Signs in and serves the mail domains page, with the mail server in the given state.
 * @param page The Playwright page.
 * @param server The status the mail server reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns The recorder of the turn-on requests.
 */
const openWith = async (
  page: Page,
  server: MailServerStatus,
  user?: AuthenticatedUser,
): Promise<MailServerStub> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMailChoices(page)
  await stubMailDomains(page, [])
  return await stubMailServer(page, server)
}

test('an administrator is told mail is off and is not offered the form that would only be refused', async ({ page }) => {
  await openWith(page, MAIL_OFF)

  await page.goto('/mail/domains')

  await expect(page.getByRole('heading', { name: 'Mail is turned off on this server' })).toBeVisible()
  await expect(page.getByLabel('Mail server host name')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add domain' })).toHaveCount(0)
  await expect(page.getByText('Ask the server administrator')).toHaveCount(0)
})

test('turning mail on sends the host name with the starting values the panel stated, then offers the form', async ({ page }) => {
  const server = await openWith(page, MAIL_OFF)
  await page.goto('/mail/domains')

  await page.getByLabel('Mail server host name').fill('mail.example.com')
  await page.getByRole('button', { name: 'Turn mail on' }).click()

  await expect(page.getByRole('button', { name: 'Add domain' })).toBeVisible()
  await expect(page.getByText('Mail is turned on.')).toBeVisible()
  expect(server.enables()).toEqual([{ hostname: 'mail.example.com', ...MAIL_STARTING_VALUES }])
})

test('an empty host name is refused by the page and nothing is sent', async ({ page }) => {
  const server = await openWith(page, MAIL_OFF)
  await page.goto('/mail/domains')

  await page.getByRole('button', { name: 'Turn mail on' }).click()

  await expect(page.getByText('Enter the host name the mail server presents.')).toBeVisible()
  expect(server.enables()).toEqual([])
})

test('an administrator whose mail is on is offered the form and no notice', async ({ page }) => {
  await openWith(page, MAIL_ON)

  await page.goto('/mail/domains')

  // Positive control for the absence below: the form is drawn.
  await expect(page.getByRole('button', { name: 'Add domain' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Mail is turned off on this server' })).toHaveCount(0)
})

test('a customer keeps the form and is never offered the administrator\'s switch', async ({ page }) => {
  await openWith(page, MAIL_OFF, CUSTOMER)

  await page.goto('/mail/domains')

  await expect(page.getByRole('button', { name: 'Add domain' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Mail is turned off on this server' })).toHaveCount(0)
})
