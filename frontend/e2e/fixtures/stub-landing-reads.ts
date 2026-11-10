import type { Page } from '@playwright/test'
import { ONE_SHOT, stubDashboard, WITHHELD_DASHBOARD } from './stub-dashboard-route'
import { DEFAULT_LAYOUT, installLiveStream, stubDashboardLayout } from './stub-live-dashboard-stream'

/** Who the landing page is drawn for. */
export type LandingAudience = 'administrator' | 'customer'

/**
 * Answers the reads the landing page (`/`) makes beyond the health check and the shell's own: the
 * one-shot dashboard read and, for an administrator, the dashboard layout and the live stream. A
 * spec that opens `/` on its way to another screen needs them, because an unanswered read reaches
 * no backend and the guard counts it. Call it before `page.goto`: the stream stand-in is an init
 * script.
 * @param page The Playwright page whose network the routes are installed on.
 * @param audience Who is signed in; the administrator by default.
 * @returns Resolves once the routes are installed.
 */
export const stubLandingReads = async (page: Page, audience: LandingAudience = 'administrator'): Promise<void> => {
  if (audience === 'customer') {
    await stubDashboard(page, WITHHELD_DASHBOARD)
    return
  }
  await stubDashboard(page, ONE_SHOT)
  await stubDashboardLayout(page, DEFAULT_LAYOUT)
  await installLiveStream(page)
}
