import type { Page } from '@playwright/test'
import type { AuthenticatedSession, AuthenticatedUser, LoginResult, SetupState } from '../../src/types/auth'

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

/**
 * The signed-in half, declared once. Both shapes below are built from it rather
 * than one being dug out of the other, so neither needs an assertion about the
 * other's contents.
 */
const SIGNED_IN_SESSION: AuthenticatedSession = {
  accessToken: ACCESS_TOKEN,
  expiresAt: '2099-01-01T00:00:00+00:00',
  user: ADMINISTRATOR,
  requiresTwoFactorSetup: false,
}

/** The login result a stubbed sign-in returns: the signed-in half in its envelope. */
const SIGNED_IN: LoginResult = { session: SIGNED_IN_SESSION }

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
      // Refresh answers with the signed-in half FLAT — no `session` envelope, because a
      // refresh has no "a factor is owed" case. Spreading the login body here instead put
      // `user` beside a nested `session` the store reads from, so a spec passing a customer
      // was silently handed the administrator: the hazard this comment used to claim was
      // fixed, reintroduced by the shape change and caught by two red specs.
      body: JSON.stringify({ ...SIGNED_IN_SESSION, user }),
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

/**
 * What a stubbed `/auth/refresh` returns, which is NOT the same shape as a sign-in.
 *
 * Refresh answers with the signed-in half flat, because it has no "a second factor is
 * owed" case — the cookie either still stands or the call fails. The two are separate
 * exports rather than one, because feeding the login body to a refresh route is a
 * mistake that costs nothing at type-check time and then quietly signs nobody in: the
 * store reads `accessToken` from the top level and finds an envelope instead.
 */
export const stubbedRefresh: AuthenticatedSession = SIGNED_IN_SESSION
