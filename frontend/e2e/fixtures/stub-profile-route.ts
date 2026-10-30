import type { Page } from '@playwright/test'
import type { Profile } from '../../src/types/profile'

/** The signed-in person's own profile endpoint. */
const PROFILE_PATTERN = /\/api\/v1\/profile(\?.*)?$/

/**
 * Fulfils `GET /api/v1/profile` with the given profile, as the panel answers any signed-in person.
 * @param page The Playwright page whose network the route is installed on.
 * @param profile The profile the stub reports as the caller's own.
 * @returns Resolves once the route is installed.
 */
export const stubProfile = async (page: Page, profile: Profile): Promise<void> => {
  await page.route(PROFILE_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(profile) })
  })
}
