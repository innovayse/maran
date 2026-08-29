import type { Page } from '@playwright/test'
import type { PanelModule } from '../../src/types/module'

/**
 * Fulfils `GET /api/v1/modules` with an empty module list — the true state
 * of the backend today (no modules exist yet) — so a spec does not depend on
 * a live backend to load the shell.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubEmptyModules = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/modules', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([]),
    })
  })
}

/**
 * Fulfils `GET /api/v1/modules` with an arbitrary catalogue, so a spec can
 * exercise licence gating (an enabled module alongside a locked one) without
 * a live backend.
 * @param page The Playwright page whose network the route is installed on.
 * @param modules The catalogue the stub reports.
 * @returns Resolves once the route is installed.
 */
export const stubModules = async (page: Page, modules: PanelModule[]): Promise<void> => {
  await page.route('**/api/v1/modules', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(modules),
    })
  })
}
