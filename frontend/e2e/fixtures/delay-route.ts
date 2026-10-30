import type { Page } from '@playwright/test'

/**
 * Holds every request matching `pattern` for `ms` milliseconds, then hands it on to whatever stub
 * the spec installed for it. Playwright runs the most recently installed route first, so this is
 * installed AFTER the stub it delays; `fallback` passes the request down to that stub unchanged.
 * Adding behaviour this way leaves every existing stub as it is: a slow answer, a slow refusal and
 * a fast one are the same stub with a different hold in front of it.
 * @param page The Playwright page whose network the route is installed on.
 * @param pattern The requests to hold, as `page.route` matches them.
 * @param ms How long to hold each one.
 * @returns Resolves once the route is installed.
 */
export const delayRoute = async (page: Page, pattern: string | RegExp, ms: number): Promise<void> => {
  await page.route(pattern, async (route) => {
    await new Promise<void>((resolve) => {
      setTimeout(resolve, ms)
    })
    await route.fallback()
  })
}
