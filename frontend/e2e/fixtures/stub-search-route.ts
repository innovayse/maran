import type { Page } from '@playwright/test'
import type { SearchResult } from '../../src/types/search'

/** The panel's global search endpoint, which the jump-to palette asks from two typed characters on. */
const SEARCH_PATTERN = /\/api\/v1\/search(\?.*)?$/

/** An answer in which the panel found nothing in any group. */
const NOTHING_FOUND: SearchResult = { groups: [] }

/**
 * Fulfils `POST /api/v1/search` with an answer that found nothing, so a spec about the palette's
 * own page list is not also a spec about the server's hits: typing into the palette asks the panel,
 * and an unanswered question reaches no backend.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubEmptySearch = async (page: Page): Promise<void> => {
  await page.route(SEARCH_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(NOTHING_FOUND) })
  })
}
