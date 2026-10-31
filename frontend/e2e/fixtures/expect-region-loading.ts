import { answerBeforeDelay, earlySkeletons, recordSkeletonFlashes, runPastDelay } from './skeleton-flash'
import { expect, type Locator, type Page } from '@playwright/test'
import { delayRoute } from './delay-route'
import { holdRoute } from './hold-route'

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
  // Looked for inside the page: the app bar's breadcrumb holds a placeholder of its own for a record
  // still on its way, and it is hidden whenever the trail does not fit the bar.
  await expect(page.getByRole('main').locator('[data-skeleton]').first()).toBeVisible()
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
  const held = await holdRoute(page, scenario.pattern)
  await recordSkeletonFlashes(page)
  await page.goto(scenario.path)

  // The page's clock is the test's (see `recordSkeletonFlashes`): the read is answered after 50 ms
  // of page time however busy the machine is, then time runs far past the 150 ms delay, and no
  // placeholder may have been seen at any point.
  await answerBeforeDelay(page, held)
  await expect(page.getByText(scenario.contentText).first()).toBeVisible()
  await runPastDelay(page)
  expect(await earlySkeletons(page)).toEqual([])
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
