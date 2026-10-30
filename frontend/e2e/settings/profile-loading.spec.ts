import { test, type Page } from '@playwright/test'
import {
  expectRegionFastLoad,
  expectRegionSlowFailure,
  expectRegionSlowLoad,
  type RegionLoadingScenario,
} from '../fixtures/expect-region-loading'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubProfile } from '../fixtures/stub-profile-route'

// The profile while it is on the way (#243): the cards keep their real terms over placeholder values.

const PROFILE_PATTERN = /\/api\/v1\/profile(\?.*)?$/

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The profile could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/** How the profile screen is opened and answered. */
const PROFILE_SCENARIO: RegionLoadingScenario = {
  stub: async (page) => {
    await shell(page)
    await stubProfile(page, {
      username: stubbedAdministrator.username,
      email: stubbedAdministrator.email,
      role: 'admin',
      isTwoFactorEnabled: true,
      activeSessionCount: 2,
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
  path: '/profile',
  anchor: (page) => {
    return page.getByText('Username', { exact: true })
  },
  contentText: stubbedAdministrator.email,
  failureText: REFUSAL,
}

test('profile: a slow answer shows placeholders under the real labels, then the content', async ({ page }) => {
  await expectRegionSlowLoad(page, PROFILE_SCENARIO)
})

test('profile: a fast answer shows no placeholder', async ({ page }) => {
  await expectRegionFastLoad(page, PROFILE_SCENARIO)
})

test('profile: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectRegionSlowFailure(page, PROFILE_SCENARIO)
})
