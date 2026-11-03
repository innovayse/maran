import type { Page } from '@playwright/test'
import type { AccountAccess, Profile } from '../../src/types/profile'

/** The signed-in person's own profile endpoint. */
const PROFILE_PATTERN = /\/api\/v1\/profile(\?.*)?$/

/**
 * A profile as a spec writes it: the account standing is optional, because most specs are about
 * the profile screen and say nothing of it. An omitted standing is `null`, which is what the panel
 * reports for an administrator.
 */
export type ProfileStub = Omit<Profile, 'accountAccess'> & { accountAccess?: AccountAccess | null }

/**
 * The profile a quiet customer shell reads: the owner of the account, so a spec that is not about
 * grants has a menu offering everything.
 */
export const OWNER_PROFILE: ProfileStub = {
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  isTwoFactorEnabled: false,
  activeSessionCount: 1,
  accountAccess: { role: 'owner', roleDisplayName: 'Owner', features: [] },
}

/**
 * Fulfils `GET /api/v1/profile` with the given profile, as the panel answers any signed-in person.
 * @param page The Playwright page whose network the route is installed on.
 * @param profile The profile the stub reports as the caller's own, or a function that builds it on
 *   each request, for a spec whose answer changes (the caller switched accounts).
 * @returns Resolves once the route is installed.
 */
export const stubProfile = async (page: Page, profile: ProfileStub | (() => ProfileStub)): Promise<void> => {
  await page.route(PROFILE_PATTERN, async (route) => {
    const answer = typeof profile === 'function' ? profile() : profile
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ accountAccess: null, ...answer }),
    })
  })
}
