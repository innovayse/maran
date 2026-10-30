import type { Page } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubProfile } from '../fixtures/stub-profile-route'

// Two-step verification while the profile is on the way (#243). Which flow the page offers is not
// known until the profile answers, so the wait holds a generic card under the real heading.

const PROFILE_PATTERN = /\/api\/v1\/profile(\?.*)?$/

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The profile could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

describeRegionLoading('two-step verification', {
  stub: async (page) => {
    await shell(page)
    await stubProfile(page, {
      username: stubbedAdministrator.username,
      email: stubbedAdministrator.email,
      role: 'admin',
      isTwoFactorEnabled: false,
      activeSessionCount: 1,
    })
  },
  stubFailure: async (page) => {
    await shell(page)
    await page.route(PROFILE_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: PROFILE_PATTERN,
  path: '/settings/two-factor',
  anchor: (page) => {
    return page.getByRole('heading', { level: 1, name: 'Two-step verification' })
  },
  contentText: 'Authenticator app',
  failureText: REFUSAL,
})
