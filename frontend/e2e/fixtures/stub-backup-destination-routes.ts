import type { Page } from '@playwright/test'

/** The one route the destinations screen reads; it takes no parameters. */
const DESTINATIONS_PATTERN = '**/api/v1/backup-destinations'

/**
 * Answers `GET /api/v1/backup-destinations` with the given rows.
 *
 * The rows are typed `unknown` rather than `BackupDestination` on purpose: one spec has to hand the
 * screen a payload carrying a member the panel's own type does NOT declare — a credential a later
 * build might start sending — and a fixture that could only express the declared shape could not
 * pose that question at all.
 * @param page The Playwright page whose network the route is installed on.
 * @param destinations The rows the stubbed panel answers with, in the order it returns them.
 * @returns Resolves once the route is installed.
 */
export const stubBackupDestinations = async (
  page: Page,
  destinations: readonly unknown[],
): Promise<void> => {
  await page.route(DESTINATIONS_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(destinations),
    })
  })
}

/**
 * Refuses `GET /api/v1/backup-destinations` with one RFC 7807 problem.
 *
 * The panel answers a signed-in customer **403** here and never 404: a destination carries no
 * account, so there is no tenant fact a 404 could conceal. The sentence is the backend's own,
 * already localized, and the screen must render it rather than a copy of its own.
 * @param page The Playwright page whose network the route is installed on.
 * @param status The HTTP status the stubbed panel refuses with.
 * @param code The machine-stable problem code.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubBackupDestinationsRefusal = async (
  page: Page,
  status: number,
  code: string,
  detail: string,
): Promise<void> => {
  await page.route(DESTINATIONS_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code, title: 'Forbidden', detail }),
    })
  })
}
