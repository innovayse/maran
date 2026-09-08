import type { Page } from '@playwright/test'
import type { PanelModule } from '../../src/types/module'

/**
 * Fulfils `GET /api/v1/modules` with an empty catalogue, so a spec about
 * something else does not depend on a live backend to load the shell.
 *
 * An empty list is a real answer, not a stand-in for one: the panel composes
 * sixteen modules today, and this fixture's comment used to say none existed
 * yet, which stopped being true long before anyone noticed. Reach for this when
 * the catalogue is irrelevant to the spec; use {@link stubModules} when it is
 * the thing under test, since an empty catalogue sends every gated route to the
 * upgrade page.
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

/**
 * Fails `GET /api/v1/modules`, so a spec can exercise the case where the SPA
 * does not know what the licence covers.
 *
 * Distinct from an empty catalogue on purpose: an empty list is the panel
 * answering "no modules", where this is the panel not answering at all, and the
 * router guard must treat the two differently — the first is a real refusal,
 * the second is a question only the backend can settle.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubModulesUnavailable = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/modules', async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Unexpected', detail: 'The module catalogue is unavailable.' }),
    })
  })
}
