import type { Page } from '@playwright/test'
import { loaded } from './stub-dashboard-route'
import { OWNER_PROFILE, stubProfile } from './stub-profile-route'
import { stubServers } from './stub-servers-route'

/** What the stubbed panel says a person may pin, when the spec has no opinion. */
const PINNED_PAGES_LIMIT = 8

/**
 * Answers the reads the shell makes on every screen with the quiet answers of a panel that has
 * nothing to show: one local server, no pinned pages, no view-as session, no needs-attention items,
 * no pending updates, no further accounts to act for, and a customer who owns their account.
 *
 * A spec that asserts a clean console needs these, because an unanswered read reaches no backend
 * and the browser logs the failure; a spec about one of them installs its own stub afterwards, and
 * the later route wins.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
export const stubShellReads = async (page: Page): Promise<void> => {
  await stubServers(page, [{ kindDisplayName: 'This server (local)', hostName: null }])

  await page.route(/\/api\/v1\/me\/pinned-pages(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], limit: PINNED_PAGES_LIMIT }),
    })
  })

  await page.route(/\/api\/v1\/view-as$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ viewing: null }) })
  })

  await page.route(/\/api\/v1\/dashboard\/attention-count(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(loaded(0)) })
  })

  await page.route(/\/api\/v1\/me\/accounts(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) })
  })

  await page.route(/\/api\/v1\/monitoring\/updates\/count(\?.*)?$/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ count: 0, securityCount: null }),
    })
  })

  await page.route(/\/api\/v1\/monitoring\/updates\/reboot-required(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ required: null }) })
  })

  // The shell asks a customer for their standing on the account; an administrator is never asked.
  await stubProfile(page, OWNER_PROFILE)
}
