import type { Page } from '@playwright/test'
import type { AuthenticatedUser, LoginResult, SetupState } from '../../src/types/auth'

/** The administrator every authenticated spec signs in as. */
const ADMINISTRATOR: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000001',
  username: 'admin',
  email: 'admin@example.com',
  role: 'admin',
  accountId: null,
}

/**
 * A token shaped like a JWT but signed by nobody. The SPA never inspects it — it
 * only puts it in an `Authorization` header — and every request that would carry
 * it is stubbed, so a real signature would prove nothing these specs are about.
 */
const ACCESS_TOKEN = 'stub.access.token'

/** The login result a stubbed sign-in or refresh returns. */
const SIGNED_IN: LoginResult = {
  accessToken: ACCESS_TOKEN,
  expiresAt: '2099-01-01T00:00:00+00:00',
  twoFactorRequired: false,
  user: ADMINISTRATOR,
}

/**
 * Fulfils the two endpoints the router's auth guard consults on every navigation,
 * so a spec about the shell is about the shell.
 *
 * Without this, every existing spec would have to sign in first — thirty
 * preambles testing the same login instead of the thing each spec was written for.
 * The auth flows themselves are covered by their own specs, which stub nothing.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
export const stubSignedIn = async (page: Page, user: AuthenticatedUser = ADMINISTRATOR): Promise<void> => {
  await stubSetupState(page, { isComplete: true })

  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      // The user is a parameter because the shell shows different things to different roles.
      // While it was hard-coded, a spec could pass a customer and silently be handed the
      // administrator instead — the assertion would then be testing nothing it claimed to.
      body: JSON.stringify({ ...SIGNED_IN, user }),
    })
  })
}

/**
 * Fulfils the guard's endpoints as a panel nobody is signed in to: setup is done,
 * and the refresh cookie is gone.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
export const stubSignedOut = async (page: Page): Promise<void> => {
  await stubSetupState(page, { isComplete: true })

  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RefreshTokenInvalidUnauthorized', detail: 'Your session has ended.' }),
    })
  })
}

/**
 * Fulfils `GET /api/v1/setup/state` with a chosen answer.
 * @param page The Playwright page whose network the route is installed on.
 * @param state Whether the panel already has an administrator.
 * @returns Resolves once the route is installed.
 */
export const stubSetupState = async (page: Page, state: SetupState): Promise<void> => {
  await page.route('**/api/v1/setup/state', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(state),
    })
  })
}

/**
 * Fulfils `POST /api/v1/auth/login` with a chosen outcome.
 * @param page The Playwright page whose network the route is installed on.
 * @param outcome What the sign-in reports, or an error to answer with.
 * @returns Resolves once the route is installed.
 */
export const stubLogin = async (
  page: Page,
  outcome: LoginResult | { status: number; code: string; detail: string },
): Promise<void> => {
  await page.route('**/api/v1/auth/login**', async (route) => {
    if ('status' in outcome) {
      await route.fulfill({
        status: outcome.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: outcome.code, detail: outcome.detail }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(outcome),
    })
  })
}

/** The signed-in administrator these fixtures report, for a spec that asserts on them. */
export const stubbedAdministrator: AuthenticatedUser = ADMINISTRATOR

/** The login result a successful stubbed sign-in returns. */
export const stubbedSignIn: LoginResult = SIGNED_IN
