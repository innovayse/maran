import type { Page } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'

// A customer's own account page while it is on the way (#243): the identity card keeps its terms
// and the limit tiles their labels, over placeholder values.

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

/** The server's refusal the failure case answers with. */
const REFUSAL = 'Your account could not be read.'

/** The customer's own account read. */
const MY_ACCOUNT_PATTERN = /\/api\/v1\/accounts\/me(\?.*)?$/

/**
 * Signs the customer in and stubs the shell and the resource summary.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubShell = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, [])
  await stubMyAccountResources(page, { accountId: OWN_ACCOUNT.id, resources: [] })
}

describeRegionLoading('my account', {
  stub: async (page) => {
    await stubShell(page)
    await stubMyAccount(page, OWN_ACCOUNT)
  },
  stubFailure: async (page) => {
    await stubShell(page)
    await page.route(MY_ACCOUNT_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: MY_ACCOUNT_PATTERN,
  path: '/my-account',
  anchor: (page) => {
    return page.getByRole('term').filter({ hasText: 'Primary domain' })
  },
  contentText: 'olive.example.com',
  failureText: REFUSAL,
})
