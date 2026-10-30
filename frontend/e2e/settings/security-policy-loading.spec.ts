import { test, type Page } from '@playwright/test'
import {
  expectRegionFastLoad,
  expectRegionSlowFailure,
  expectRegionSlowLoad,
  type RegionLoadingScenario,
} from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { SecurityPolicy } from '../../src/types/securityPolicy'

// The security policy form while the policy is on the way (#243): real labels over placeholder controls.

const POLICY_PATTERN = '**/api/v1/security-policy'

const POLICY: SecurityPolicy = {
  minimumPasswordLength: 12,
  forceTwoFactorForAdmins: true,
  maxFailedLoginAttempts: 5,
  lockoutMinutes: 15,
}

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The security policy could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/** How the security policy screen is opened and answered. */
const SECURITY_POLICY_SCENARIO: RegionLoadingScenario = {
  stub: async (page) => {
    await shell(page)
    await page.route(POLICY_PATTERN, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(POLICY) })
    })
  },
  stubFailure: async (page) => {
    await shell(page)
    await page.route(POLICY_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: POLICY_PATTERN,
  path: '/settings/security-policy',
  anchor: (page) => {
    return page.getByText('Minimum password length', { exact: true })
  },
  // The policy is all field values, which are not text; the loaded state is observed by the helper
  // through the status turning "Loaded" and every placeholder leaving.
  contentText: 'Save policy',
  failureText: REFUSAL,
}

test('security policy: a slow answer shows placeholders under the real labels, then the content', async ({ page }) => {
  await expectRegionSlowLoad(page, SECURITY_POLICY_SCENARIO)
})

test('security policy: a fast answer shows no placeholder', async ({ page }) => {
  await expectRegionFastLoad(page, SECURITY_POLICY_SCENARIO)
})

test('security policy: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectRegionSlowFailure(page, SECURITY_POLICY_SCENARIO)
})
