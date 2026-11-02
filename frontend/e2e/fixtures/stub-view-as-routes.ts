import type { Page } from '@playwright/test'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { ViewAsSession, ViewingAs } from '../../src/types/viewAs'

/** The token the stubbed panel issues for a view-as session; distinct from the administrator's. */
export const VIEW_AS_TOKEN = 'stub.view-as.token'

/** Records what the stubbed view-as endpoint was asked. */
export interface ViewAsCalls {
  /** How many times a view-as session was started. */
  started: number
  /** How many times one was ended. */
  ended: number
}

/**
 * Stubs `/api/v1/view-as` like the panel: a start issues {@link VIEW_AS_TOKEN} for `viewedAs`, the
 * state read answers "viewing" only for a request carrying that token while it has not been ended,
 * and the end marks it ended. The banner is therefore drawn from what this stub reports per token,
 * which is the property under test — not from anything the SPA remembers.
 * @param page The Playwright page whose network the route is installed on.
 * @param viewedAs The customer login the token signs in as.
 * @param viewing What the panel says about the session.
 * @returns A live view of the calls made.
 */
export const stubViewAs = async (page: Page, viewedAs: AuthenticatedUser, viewing: ViewingAs): Promise<ViewAsCalls> => {
  const calls: ViewAsCalls = { started: 0, ended: 0 }

  await page.route('**/api/v1/view-as', async (route) => {
    const method = route.request().method()
    const isViewAsToken = route.request().headers()['authorization'] === `Bearer ${VIEW_AS_TOKEN}`

    if (method === 'POST') {
      calls.started += 1
      const session: ViewAsSession = { accessToken: VIEW_AS_TOKEN, expiresAt: viewing.expiresAt, user: viewedAs, viewing }
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(session) })
      return
    }

    if (method === 'DELETE') {
      calls.ended += 1
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    const live = isViewAsToken && calls.ended === 0
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ viewing: live ? viewing : null }),
    })
  })

  return calls
}

/**
 * Stubs a refused start: the panel answers 404 with its own sentence, as it does for an account
 * outside the caller's scope.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The sentence the panel sends.
 * @returns Resolves once the route is installed.
 */
export const stubViewAsRefused = async (page: Page, detail: string): Promise<void> => {
  await page.route('**/api/v1/view-as', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: detail, detail, status: 404, code: 'ViewAsAccountNotFound' }),
      })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ viewing: null }) })
  })
}
