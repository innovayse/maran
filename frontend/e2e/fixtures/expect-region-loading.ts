import { earlySkeletons, recordSkeletonFlashes } from './skeleton-flash'
import { expect, type Locator, type Page } from '@playwright/test'
import { delayRoute } from './delay-route'

/** How one card, detail or form screen is opened and answered, for the `expectRegion*` checks. */
export interface RegionLoadingScenario {
  /** Installs every stub the screen needs with a successful answer for the read being delayed. */
  stub: (page: Page) => Promise<void>
  /** Installs every stub the screen needs with that read failing. */
  stubFailure: (page: Page) => Promise<void>
  /** The read the delay holds. */
  pattern: string | RegExp
  /** The screen's address. */
  path: string
  /**
   * Something the screen draws while loading and keeps once loaded (a real label, a term, a card
   * heading), whose box must not move when the content lands.
   */
  anchor: (page: Page) => Locator
  /** A text the loaded content shows. */
  contentText: string
  /** A text the failure's alert shows. */
  failureText: string
}

/** A wait long enough for the placeholders to show (they appear after 150 ms). */
const SLOW_MS = 600

/** A wait short enough that no placeholder may ever show. */
const FAST_MS = 50

/**
 * Where the anchor sits.
 * @param locator The anchor.
 * @returns Its top and height, in CSS pixels.
 */
const boxOf = async (locator: Locator): Promise<{ y: number; height: number }> => {
  const box = await locator.boundingBox()
  expect(box).not.toBeNull()
  return { y: box?.y ?? 0, height: box?.height ?? 0 }
}

/**
 * Asserts the slow path (#243): placeholders show under the real header or labels with one polite
 * status, then the content lands and nothing above it moved.
 * Called from the spec's own `test`, so Playwright attributes the test to that spec, not to this file.
 * @param page The Playwright page.
 * @param scenario How the screen is opened and answered.
 * @returns Resolves once every assertion held.
 */
export const expectRegionSlowLoad = async (page: Page, scenario: RegionLoadingScenario): Promise<void> => {
  await scenario.stub(page)
  await delayRoute(page, scenario.pattern, SLOW_MS)
  await page.goto(scenario.path)

  const anchor = scenario.anchor(page)
  // A screen may hold more than one region (the status page reads its health verdict in a region
  // of its own, which settles independently), so the wait is announced by SOME region, and once
  // the content has landed NO region still says it is loading.
  const statuses = page.getByTestId('loading-status')
  await expect(anchor).toBeVisible()
  await expect(page.locator('[data-skeleton]').first()).toBeVisible()
  await expect(statuses.filter({ hasText: 'Loading…' }).first()).toBeAttached()
  const before = await boxOf(anchor)

  await expect(page.getByText(scenario.contentText).first()).toBeVisible()
  await expect(statuses.filter({ hasText: 'Loading…' })).toHaveCount(0)
  await expect(statuses.filter({ hasText: 'Loaded' }).first()).toBeAttached()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  // The label the placeholders stood beside is where the content lands: nothing above it moved.
  expect(await boxOf(anchor)).toEqual(before)
}

/**
 * Asserts the fast path (#243): an answer quicker than the 150 ms delay never shows a placeholder.
 * Called from the spec's own `test`, so Playwright attributes the test to that spec, not to this file.
 * @param page The Playwright page.
 * @param scenario How the screen is opened and answered.
 * @returns Resolves once every assertion held.
 */
export const expectRegionFastLoad = async (page: Page, scenario: RegionLoadingScenario): Promise<void> => {
  await scenario.stub(page)
  await delayRoute(page, scenario.pattern, FAST_MS)
  await recordSkeletonFlashes(page)
  await page.goto(scenario.path)

  // Every frame from the first, for longer than the answer and the 150 ms delay together: no
  // placeholder may be seen in the first 150 ms of its wait, although they are mounted to hold
  // the space (see `recordSkeletonFlashes` for why the wait, not the stub, is the clock).
  await page.waitForTimeout(400)
  expect(await earlySkeletons(page)).toEqual([])
  await expect(page.getByText(scenario.contentText).first()).toBeVisible()
}

/**
 * Asserts the failure path (#243): a slow refusal is an alert, never a skeleton.
 * Called from the spec's own `test`, so Playwright attributes the test to that spec, not to this file.
 * @param page The Playwright page.
 * @param scenario How the screen is opened and answered.
 * @returns Resolves once every assertion held.
 */
export const expectRegionSlowFailure = async (page: Page, scenario: RegionLoadingScenario): Promise<void> => {
  await scenario.stubFailure(page)
  await delayRoute(page, scenario.pattern, SLOW_MS)
  await page.goto(scenario.path)

  await expect(page.getByText(scenario.failureText)).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
}
