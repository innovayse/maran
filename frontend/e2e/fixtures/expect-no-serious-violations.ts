import { AxeBuilder } from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'

/**
 * The axe impacts this suite refuses. `minor` and `moderate` findings are reported by axe but do
 * not fail a run: the closing criterion of issue #149 is "no serious violations", and a gate that
 * fails on every advisory finding is a gate people learn to switch off.
 */
const REFUSED_IMPACTS: readonly string[] = ['serious', 'critical']

/**
 * The WCAG rule sets the scan runs: 2.0, 2.1 and 2.2 at levels A and AA. Named rather than left to
 * axe's default so a release of axe that widens its default set does not silently change the gate.
 */
const WCAG_TAGS: readonly string[] = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']

/**
 * Runs axe over the whole document as it is drawn now and lists every serious or critical
 * violation, each with the selectors of its nodes, so a red run says what to fix.
 *
 * The scan observes the rendered DOM in a real browser, after the page's own data has landed: a
 * caller waits for the screen's content before calling, or the scan reads a skeleton and passes
 * over markup the reader never gets.
 *
 * It also waits until no CSS animation or transition is running. A banner or dialog that is
 * fading in is "visible" from its first frame, and axe measures the colours it is drawn with at
 * that instant, blended with whatever is behind it: a contrast failure the settled page does not
 * have, and one that came and went between a first attempt and its retry (issue #401). Waiting on
 * the browser's own animation list needs no sleep and ends the moment the page is still.
 * @param page The Playwright page under test.
 * @returns One line per refused violation; empty when there is none.
 */
export const seriousViolations = async (page: Page): Promise<string[]> => {
  // A transition that has been asked for starts on the NEXT frame (Vue applies its enter class, then
  // the active class a frame later), so an empty animation list read in the same frame as the click
  // that opened a dialog says nothing yet. Two frames pass first; then the list must be empty.
  await page.evaluate(() => {
    return new Promise<void>((resolve) => {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          resolve()
        })
      })
    })
  })
  await page.waitForFunction(() => {return document.getAnimations().length === 0})
  const results = await new AxeBuilder({ page }).withTags([...WCAG_TAGS]).analyze()
  // Vacuity guard on the axis that can go blind: a scan that checked no rule at all would report
  // no violation too. axe lists what it ran in `passes`, so a real scan always has some.
  expect(results.passes.length).toBeGreaterThan(0)
  return results.violations
    .filter((violation) => {return REFUSED_IMPACTS.includes(violation.impact ?? '')})
    .map((violation) => {
      const targets = violation.nodes.map((node) => {return node.target.join(' ')}).join(', ')
      return `${violation.id} (${violation.impact ?? 'unknown'}): ${violation.help} — ${targets}`
    })
}

/**
 * Fails naming every serious or critical violation axe finds on the page as drawn now.
 * @param page The Playwright page under test.
 * @returns Resolves once the scan found nothing it refuses.
 */
export const expectNoSeriousViolations = async (page: Page): Promise<void> => {
  expect(await seriousViolations(page)).toEqual([])
}
