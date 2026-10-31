import { answerBeforeDelay, earlySkeletons, recordSkeletonFlashes, runPastDelay } from './skeleton-flash'
import { expect, type Page } from '@playwright/test'
import { delayRoute } from './delay-route'
import { holdRoute } from './hold-route'

/** How one table screen is opened and answered, for the `expectTable*` checks. */
export interface TableLoadingScenario {
  /** Installs every stub the screen needs with a successful answer for its list. */
  stub: (page: Page) => Promise<void>
  /** Installs every stub the screen needs with the list's read failing. */
  stubFailure: (page: Page) => Promise<void>
  /** The list read the delay holds. */
  pattern: string | RegExp
  /** The screen's address. */
  path: string
  /** The table's accessible name (its caption). */
  table: string
  /** A text one of the loaded rows shows. */
  rowText: string
  /** A text the failure's alert shows. */
  failureText: string
}

/** A wait long enough for the placeholders to show (they appear after 150 ms). */
const SLOW_MS = 600

/**
 * Where the table's header row sits.
 * @param page The Playwright page.
 * @param table The table's accessible name.
 * @returns The header row's top and height, in CSS pixels.
 */
const headerBox = async (page: Page, table: string): Promise<{ y: number; height: number }> => {
  const box = await page.getByRole('table', { name: table }).locator('thead').boundingBox()
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
export const expectTableSlowLoad = async (page: Page, scenario: TableLoadingScenario): Promise<void> => {
  await scenario.stub(page)
  await delayRoute(page, scenario.pattern, SLOW_MS)
  await page.goto(scenario.path)

  const table = page.getByRole('table', { name: scenario.table })
  await expect(table.getByRole('columnheader').first()).toBeVisible()
  await expect(page.locator('[data-skeleton^="cell-"]').first()).toBeVisible()
  await expect(page.getByTestId('loading-status')).toHaveText('Loading…')
  const before = await headerBox(page, scenario.table)

  await expect(table.getByText(scenario.rowText).first()).toBeVisible()
  await expect(page.getByTestId('loading-status')).toHaveText('Loaded')
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  // The header the placeholders stood under is the header the rows land under: nothing above
  // the rows moves when they arrive.
  expect(await headerBox(page, scenario.table)).toEqual(before)
}

/**
 * Asserts the fast path (#243): an answer quicker than the 150 ms delay never shows a placeholder.
 * Called from the spec's own `test`, so Playwright attributes the test to that spec, not to this file.
 * @param page The Playwright page.
 * @param scenario How the screen is opened and answered.
 * @returns Resolves once every assertion held.
 */
export const expectTableFastLoad = async (page: Page, scenario: TableLoadingScenario): Promise<void> => {
  await scenario.stub(page)
  const held = await holdRoute(page, scenario.pattern)
  await recordSkeletonFlashes(page)
  await page.goto(scenario.path)

  // The page's clock is the test's (see `recordSkeletonFlashes`): the read is answered after 50 ms
  // of page time however busy the machine is, then time runs far past the 150 ms delay, and no
  // placeholder may have been seen at any point.
  await answerBeforeDelay(page, held)
  await expect(page.getByRole('table', { name: scenario.table }).getByText(scenario.rowText).first()).toBeVisible()
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
export const expectTableSlowFailure = async (page: Page, scenario: TableLoadingScenario): Promise<void> => {
  await scenario.stubFailure(page)
  await delayRoute(page, scenario.pattern, SLOW_MS)
  await page.goto(scenario.path)

  await expect(page.getByText(scenario.failureText)).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByRole('table', { name: scenario.table })).toHaveCount(0)
}
