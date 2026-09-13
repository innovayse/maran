import type { Page } from '@playwright/test'
import { stubSetupState } from './stub-auth-routes'
import type { AuthenticatedUser } from '../../src/types/auth'

/** The administrator a steered sign-in reports. */
const STEERED_ADMINISTRATOR: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000001',
  username: 'admin',
  email: 'admin@example.com',
  role: 'admin',
  accountId: null,
}

/**
 * Fulfils `POST /api/v1/auth/forgot-password` the way the panel does: 200 and a
 * bare `true`, for every address.
 *
 * There is deliberately no parameter for "does this address exist". The endpoint
 * cannot tell a caller, so neither can this fixture, and a spec that wants two
 * addresses calls it twice with the same answer — which is the point being tested.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubForgotPasswordAccepted = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/auth/forgot-password', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

/**
 * Fulfils `POST /api/v1/auth/reset-password` with the panel's one refusal — the
 * same answer a token that never existed, one that expired and one already spent
 * all receive.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The refusal text, as the backend localized it.
 * @returns Resolves once the route is installed.
 */
export const stubResetPasswordRefused = async (page: Page, detail: string): Promise<void> => {
  await page.route('**/api/v1/auth/reset-password', async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'PasswordResetTokenInvalid', detail }),
    })
  })
}

/**
 * Signs a spec in as an administrator the panel is steering into two-factor
 * enrolment: there IS a token, and it reaches nothing but the enrolment endpoints.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
export const stubSteeredAdministrator = async (page: Page): Promise<void> => {
  await stubSetupState(page, { isComplete: true })

  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'stub.access.token',
        expiresAt: '2099-01-01T00:00:00+00:00',
        user: STEERED_ADMINISTRATOR,
        requiresTwoFactorSetup: true,
      }),
    })
  })
}
