import type { Page } from '@playwright/test'
import { stubMyAccount } from './stub-my-account-route'

/**
 * Answers the customer shell's read of the account it acts for (`GET /api/v1/accounts/me`) with a
 * plain active account on a plan that reports no feature list, so the menu offers every item. A
 * spec about something else signs a customer in and needs this read answered, because the shell
 * asks for it on every screen (`useNavigationState`) and an unanswered read reaches no backend.
 * @param page The Playwright page whose network the route is installed on.
 * @param accountId The account the signed-in customer acts for.
 * @returns Resolves once the route is installed.
 */
export const stubQuietOwnAccount = async (page: Page, accountId: string): Promise<void> => {
  await stubMyAccount(page, {
    id: accountId,
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
  })
}
