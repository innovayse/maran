import type { Page } from '@playwright/test'

/** A read held at the network until the test lets it through, installed by {@link holdRoute}. */
export interface HeldRoute {
  /** Resolves once the page has asked for the held read, so the page is known to be waiting on it. */
  requested: Promise<void>
  /** Lets the held read (and every later one matching the pattern) through to its stub. */
  release: () => void
}

/**
 * Holds every request matching `pattern` until the test calls `release`, then hands it on to
 * whatever stub the spec installed for it (`fallback`, so it is installed AFTER that stub, exactly
 * like `delayRoute`). Where `delayRoute` holds a read for a span of wall-clock time, this holds it
 * for as long as the test says: an answer that must arrive before something else happens is then
 * ordered by the test, not by how busy the machine is.
 * @param page The Playwright page whose network the route is installed on.
 * @param pattern The requests to hold, as `page.route` matches them.
 * @returns The hold: when the read was asked for, and how to let it through.
 */
export const holdRoute = async (page: Page, pattern: string | RegExp): Promise<HeldRoute> => {
  let markRequested: () => void = (): void => {
    // Replaced below, before the route can call it.
  }
  let release: () => void = (): void => {
    // Replaced below, before the caller can call it.
  }
  const requested = new Promise<void>((resolve) => {
    markRequested = resolve
  })
  const released = new Promise<void>((resolve) => {
    release = resolve
  })

  await page.route(pattern, async (route) => {
    markRequested()
    await released
    await route.fallback()
  })

  return { requested, release }
}
