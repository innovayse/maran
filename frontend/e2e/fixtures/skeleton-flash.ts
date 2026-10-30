import type { Page } from '@playwright/test'

/** The window property the recorder keeps its findings in. */
const FINDINGS = '__maranSkeletonFlashes'

/** How long a wait lasts before its placeholders may be seen (`useDelayedFlag`'s default). */
const DELAY_MS = 150

/**
 * Records, on every animation frame from the first one, each placeholder a reader could see before
 * the wait it stands for had lasted {@link DELAY_MS}: one inside a loading region whose wait began
 * less than that long ago, or one outside any region (which nothing would ever hide).
 *
 * What is measured is the promise the loading kit makes, "no placeholder in the first 150 ms of a
 * wait", rather than "no placeholder for a stub that answers in 50 ms": with many workers sharing
 * one machine, a stub's answer reaches the page later than it was sent, and the page is then right
 * to show its placeholders. Each region's wait is timed from the moment it entered the DOM in its
 * loading state (a mutation observer, so the start is not a frame late), and the recorder does no
 * polling from the test process, so it does not slow the page it is watching.
 * @param page The Playwright page; call before navigating.
 * @returns Resolves once the recorder is installed.
 */
export const recordSkeletonFlashes = async (page: Page): Promise<void> => {
  await page.addInitScript(
    ({ key, delay }: { key: string; delay: number }) => {
      const findings: string[] = []
      Reflect.set(window, key, findings)
      const waitStarted = new WeakMap<Element, number>()

      const noteRegions = (): void => {
        const now = performance.now()
        for (const region of document.querySelectorAll('[data-testid="loading-region"]')) {
          if (region.getAttribute('data-state') === 'loading') {
            if (!waitStarted.has(region)) {
              waitStarted.set(region, now)
            }
          } else {
            waitStarted.delete(region)
          }
        }
      }

      new MutationObserver(noteRegions).observe(document, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ['data-state'],
      })

      const sample = (): void => {
        noteRegions()
        const now = performance.now()
        for (const element of document.querySelectorAll('[data-skeleton]')) {
          if (getComputedStyle(element).visibility !== 'visible') {
            continue
          }
          const region = element.closest('[data-testid="loading-region"]')
          const started = region === null ? undefined : waitStarted.get(region)
          if (started === undefined || now - started < delay) {
            findings.push(`${element.getAttribute('data-skeleton') ?? ''} at ${Math.round(now)} ms`)
          }
        }
        requestAnimationFrame(sample)
      }
      requestAnimationFrame(sample)
    },
    { key: FINDINGS, delay: DELAY_MS },
  )
}

/**
 * The placeholders seen too early since the page loaded, as recorded by {@link recordSkeletonFlashes}.
 * @param page The Playwright page the recorder was installed on.
 * @returns One line per early placeholder seen; empty when there was none.
 */
export const earlySkeletons = async (page: Page): Promise<string[]> => {
  return await page.evaluate((key: string) => {
    const findings: unknown = Reflect.get(window, key)
    return Array.isArray(findings) ? findings.map(String) : []
  }, FINDINGS)
}
