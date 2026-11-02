import type { Page } from '@playwright/test'
import type { HealthResponse } from '../../src/types/system'

/**
 * Fulfils `GET /health` with a healthy body, so a spec can assert the status
 * page's "ok" state without a live backend.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubHealthy = async (page: Page): Promise<void> => {
  await page.route('**/health', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ agent: 'connected', database: 'reachable' } satisfies HealthResponse),
    })
  })
}

/**
 * Aborts `GET /health` at the network level (no response reaches the app),
 * so a spec can assert the frontend-owned "unreachable" fallback — the one
 * case with no server-provided message to render.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubHealthUnreachable = async (page: Page): Promise<void> => {
  await page.route('**/health', async (route) => {
    await route.abort('connectionrefused')
  })
}

/**
 * Fulfils `GET /health` with an RFC 7807 problem+json error body, so a spec
 * can assert the backend's own message is rendered verbatim (rules/vue.md:
 * "the backend owns their text").
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param status HTTP status code the stub responds with (defaults to 503).
 * @returns Resolves once the route is installed.
 */
export const stubHealthProblem = async (page: Page, detail: string, status = 503): Promise<void> => {
  await page.route('**/health', async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'agent_unavailable', title: 'Service unavailable', detail }),
    })
  })
}
