import { expect, type Page } from '@playwright/test'
import type { HeldRoute } from './hold-route'

/** The window property the recorder keeps its findings in. */
const FINDINGS = '__maranSkeletonFlashes'

/** How long a wait lasts before its placeholders may be seen (`useDelayedFlag`'s default). */
const DELAY_MS = 150

/** How much page time passes while the read is still on its way: well inside {@link DELAY_MS}. */
const FAST_MS = DELAY_MS / 3

/** Page time run once everything has landed: far past {@link DELAY_MS}, so any timer left behind fires. */
const AFTER_MS = DELAY_MS * 10

/**
 * Freezes the page's clock and records every placeholder that becomes visible from the first
 * mutation on.
 *
 * The promise the loading kit makes is "no placeholder before a wait has lasted 150 ms" (every
 * placeholder, in a region or the app bar's breadcrumb, waits on `useDelayedFlag`). It used to be
 * checked against the wall clock, and with many workers sharing one machine a stub "answering in
 * 50 ms" reached the page much later, so a placeholder that had honestly waited its 150 ms was
 * reported as a flash (issue #265). Here the page's timers only move when the test runs them
 * ({@link answerBeforeDelay}, {@link runPastDelay}), so whatever the load, page time stays under
 * the delay until the answer has landed, and ANY placeholder seen in that span is a real flash.
 *
 * The clock is installed paused, so animation frames are frozen too: the recorder reacts to DOM
 * mutations instead (a placeholder turns visible by losing `invisible` or by being inserted), which
 * a frozen clock does not hold back.
 * @param page The Playwright page; call before navigating.
 * @returns Resolves once the clock and the recorder are installed.
 */
export const recordSkeletonFlashes = async (page: Page): Promise<void> => {
  await page.clock.install()
  await page.clock.pauseAt(Date.now() + 1_000)
  await page.addInitScript((key: string) => {
    const findings: string[] = []
    Reflect.set(window, key, findings)

    const sample = (): void => {
      for (const element of document.querySelectorAll('[data-skeleton]')) {
        if (getComputedStyle(element).visibility === 'visible') {
          const where = element.closest('[data-ui], [data-testid]')
          const owner = where?.getAttribute('data-ui') ?? where?.getAttribute('data-testid') ?? 'page'
          findings.push(`${element.getAttribute('data-skeleton') ?? ''} in ${owner}`)
        }
      }
    }

    new MutationObserver(sample).observe(document, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['class', 'data-state'],
    })
  }, FINDINGS)
}

/**
 * Lets {@link FAST_MS} of page time pass while the held read is still on its way, then lets it
 * through: an answer that, in the page's own time, came before the delay was up.
 * @param page The Playwright page the recorder was installed on.
 * @param held The read being answered.
 * @returns Resolves once the read has been released.
 */
export const answerBeforeDelay = async (page: Page, held: HeldRoute): Promise<void> => {
  await held.requested
  await page.clock.runFor(FAST_MS)
  held.release()
}

/**
 * Once no region is loading any more, runs page time far past the delay: a delayed placeholder
 * whose timer survived the answer would turn visible now, and the recorder would see it.
 * @param page The Playwright page the recorder was installed on.
 * @returns Resolves once the time has run.
 */
export const runPastDelay = async (page: Page): Promise<void> => {
  await expect(page.locator('[data-testid="loading-region"][data-state="loading"]')).toHaveCount(0)
  await page.clock.runFor(AFTER_MS)
}

/**
 * The placeholders seen since the page loaded, as recorded by {@link recordSkeletonFlashes}.
 * @param page The Playwright page the recorder was installed on.
 * @returns One line per placeholder seen; empty when there was none.
 */
export const earlySkeletons = async (page: Page): Promise<string[]> => {
  return await page.evaluate((key: string) => {
    const findings: unknown = Reflect.get(window, key)
    return Array.isArray(findings) ? [...new Set(findings.map(String))] : []
  }, FINDINGS)
}
