import { expect, type Page } from '@playwright/test'
import { FULL_CATALOGUE } from './open-full-shell'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubLandingReads } from './stub-landing-reads'
import { stubModules } from './stub-modules-route'
import { stubQuietScreens } from './stub-quiet-screens'
import { stubShellReads } from './stub-shell-reads'
import { stubApps, type AppsRecorder, type AppsWorld } from './stub-apps-routes'
import { READY_STACK, stubStack } from './stub-stack-routes'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { StackStatus } from '../../src/types/hostApp'

/**
 * Signs in on a desktop, installs the apps stubs for a world, and opens an address.
 *
 * It repeats the steps of `openFullShell` instead of calling it, because the stack status must be
 * stubbed AFTER `stubSignedIn` (whose quiet ready host would otherwise win, Playwright matching
 * routes newest-first) and BEFORE the first navigation, which is when the stack guard reads it.
 * @param page The Playwright page under test.
 * @param world The state the stubbed apps endpoints answer from.
 * @param options Where to open, who signs in, and what the host's stack status is.
 * @param options.path The address to open; the app store by default.
 * @param options.user The signed-in user; the administrator by default.
 * @param options.prepare Installs further stubs before the first navigation, for a screen of its own.
 * @param options.stack The stack status the host reports, a function producing it per request, or
 * `'unreadable'` for a panel that answers the stack read with 503.
 * @returns What the stubs recorded.
 */
export const openApps = async (
  page: Page,
  world: AppsWorld,
  options: {
    path?: string
    user?: AuthenticatedUser
    stack?: StackStatus | (() => StackStatus) | 'unreadable'
    prepare?: (page: Page) => Promise<void>
  } = {},
): Promise<AppsRecorder> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubQuietScreens(page)
  const recorder = await stubApps(page, world)
  await stubSignedIn(page, options.user)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  if (options.stack === 'unreadable') {
    await page.route('**/api/v1/apps/stack', async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'ServiceUnavailable', detail: 'The panel could not answer.' }),
      })
    })
  } else {
    await stubStack(page, options.stack ?? READY_STACK)
  }
  await options.prepare?.(page)
  await page.goto(options.path ?? '/apps')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  return recorder
}
