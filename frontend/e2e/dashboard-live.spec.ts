import { expect, test } from './fixtures/guarded-test'
import { goLive, openLiveDashboard } from './fixtures/open-live-dashboard'
import { loaded } from './fixtures/stub-dashboard-route'
import { endStream, HELLO, pushFrame, reading, streamOpens, totals } from './fixtures/stub-live-dashboard-stream'
import type { Page } from '@playwright/test'

// Issue #199 (slice S1): the administrator's dashboard is live. One stream feeds the processor,
// memory, disk and network widgets and the totals, services and activity widgets; the
// page shows whether the figures are arriving, reconnects when the connection drops, closes the
// connection in a hidden tab and continues from the newest reading it holds when it returns.
//
// BLIND SPOT: the stream is a stand-in `fetch` (e2e/fixtures/stub-live-dashboard-stream.ts), so
// this file proves what the SPA does with frames and nothing about the panel's side of the stream
// (framing on the wire, the 15-minute cap, the per-user limit, tenant scoping). Those belong to the
// backend integration tests and to the owner-run pass against a real panel.

/** The cards' processor figure. */
const cpuCard = (page: Page): ReturnType<Page['getByTestId']> => {
  return page.getByTestId('metric-card-cpu')
}

/**
 * How many stream requests the page has made so far.
 * @param page The page under test.
 * @returns The count.
 */
const openCount = async (page: Page): Promise<number> => {
  return (await streamOpens(page)).length
}

/**
 * How many connected runs the processor chart's line has (each run starts with one move command).
 * @param page The page under test.
 * @returns The count; 0 while there is no line yet.
 */
const lineRuns = async (page: Page): Promise<number> => {
  const d = (await cpuCard(page).locator('path.ui-live-line').getAttribute('d')) ?? ''
  return d.split('M').length - 1
}

/**
 * Makes the tab hidden or visible the way a browser does: the document says so and tells the page.
 * @param page The page under test.
 * @param state The visibility to take.
 * @returns Resolves once the page has been told.
 */
const setVisibility = async (page: Page, state: 'hidden' | 'visible'): Promise<void> => {
  await page.evaluate((next) => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: next })
    document.dispatchEvent(new Event('visibilitychange'))
  }, state)
}

/**
 * Starts recording every value the status pill takes, so a spec can say a state NEVER appeared.
 * @param page The page under test.
 * @returns Resolves once the recorder is installed.
 */
const recordStatuses = async (page: Page): Promise<void> => {
  await page.evaluate(() => {
    const seen: string[] = []
    ;(window as unknown as { __statuses: string[] }).__statuses = seen
    const note = (): void => {
      const pill = document.querySelector('[data-testid=live-status]')
      const status = pill?.getAttribute('data-status')
      if (status !== null && status !== undefined && seen[seen.length - 1] !== status) {
        seen.push(status)
      }
    }
    note()
    new MutationObserver(note).observe(document.body, { subtree: true, attributes: true, childList: true })
  })
}

/**
 * Reads back the statuses {@link recordStatuses} saw.
 * @param page The page under test.
 * @returns The distinct consecutive statuses, oldest first.
 */
const statusesSeen = async (page: Page): Promise<string[]> => {
  return page.evaluate(() => {
    return (window as unknown as { __statuses: string[] }).__statuses
  })
}

test.describe('the live dashboard', () => {
  test('the one-shot figures stand in until the stream delivers, then the live ones replace them', async ({ page }) => {
    await openLiveDashboard(page)

    // Before any frame: the one-shot resource card, with ITS figure (11.1), and no live widget.
    await expect(page.getByText('11.1%')).toBeVisible()
    await expect(cpuCard(page)).toHaveCount(0)

    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))

    // After: the live processor widget with the stream's figure (37.4), the one-shot card gone.
    await expect(cpuCard(page)).toContainText('37.4%')
    await expect(page.getByText('11.1%')).toHaveCount(0)
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'live')
    await expect(page.getByTestId('live-status')).toHaveText('Live')
  })

  test('a figure changes when a new reading arrives, without a reload', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))
    await expect(cpuCard(page)).toContainText('37.4%')

    await pushFrame(page, 'metrics', reading(8, { cpu: { total: 52.8, load: [1.5, 1.25, 1] } }))
    await expect(cpuCard(page)).toContainText('52.8%')
    await expect(cpuCard(page)).toContainText('Load 1.50 · 1.25 · 1.00')
    // The headline figure alone: the card also holds the chart's table of the latest readings, which
    // still lists the earlier 37.4% beside the new one.
    await expect(page.getByTestId('metric-head-cpu')).not.toContainText('37.4%')

    // The other three resource widgets are fed by the same stream.
    await expect(page.getByTestId('metric-card-memory')).toContainText('3 GiB of 8 GiB')
    await expect(page.getByTestId('metric-card-disk')).toContainText('44 GiB of 98 GiB')
    await expect(page.getByTestId('metric-card-network')).toContainText('Received 3.0 KiB/s · sent 1.0 KiB/s')
  })

  test('totals, services and activity follow what the stream pushes', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)

    await expect(page.getByRole('link', { name: /^3 Accounts/ })).toBeVisible()
    await expect(page.getByText('Sign-in failed')).toBeVisible()

    await pushFrame(
      page,
      'totals',
      totals({ counts: { accounts: loaded(9), sites: loaded(7), databases: loaded(4) } }),
    )
    await expect(page.getByRole('link', { name: /^9 Accounts/ })).toBeVisible()
    await expect(page.getByRole('link', { name: /^3 Accounts/ })).toHaveCount(0)
  })

  test('a count the panel could not take says so and is never drawn as zero', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(
      page,
      'totals',
      totals({ counts: { accounts: loaded(3), sites: { state: 'unavailable' }, databases: loaded(4) } }),
    )

    await expect(page.getByRole('link', { name: /^Unavailable Sites/ })).toBeVisible()
    await expect(page.getByRole('link', { name: /^0 Sites/ })).toHaveCount(0)
    // The inverse control: a count that WAS read is drawn as its number.
    await expect(page.getByRole('link', { name: /^3 Accounts/ })).toBeVisible()
  })

  test('a reading without a rate or a used figure is a gap, never a zero', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    // The first live reading has no network rate, and a stored-sample reading has no used disk figure.
    await pushFrame(page, 'metrics', {
      ...reading(6),
      disk: { used: null, total: 98 * 1024 * 1024 * 1024 },
      network: { rxBps: null, txBps: null },
      cpu: { total: 37.4, load: [0.5, null, null] },
    })

    await expect(page.getByTestId('metric-card-disk')).toContainText('98 GiB in size')
    await expect(page.getByTestId('metric-card-network')).not.toContainText('0.0 B/s')
    await expect(page.getByTestId('metric-card-cpu')).toContainText('Load 0.50 · — · —')
  })

  test('a dropped connection reads reconnecting, then continues from the newest reading it holds', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'live')
    expect(await streamOpens(page)).toEqual([''])

    await endStream(page)
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'reconnecting')
    await expect(page.getByTestId('live-status')).toHaveText('Reconnecting')

    // The first reconnect waits one second; the new request asks to continue after the newest
    // reading held (10:00:04), so the panel sends only what is missing.
    await expect.poll(async () => {
      return openCount(page)
    }, { timeout: 5000 }).toBe(2)
    expect((await streamOpens(page))[1]).toBe('?since=2026-11-07T10%3A00%3A04.000Z')

    await pushFrame(page, 'hello', HELLO)
    // The back-fill overlaps what is held (10:00:04) and adds 10:00:06: held once, not twice.
    await pushFrame(page, 'history', { points: [reading(4), reading(6)] })
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'live')
    await expect(cpuCard(page).locator('tbody tr')).toHaveCount(4)
  })

  test('a renewal reopens the stream at once and the viewer never sees reconnecting', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'live')
    await recordStatuses(page)

    await pushFrame(page, 'end', { reason: 'renew' })
    await endStream(page)

    // Immediately, not after the one-second reconnect wait: a plain drop needs 1000 ms.
    await expect.poll(async () => {
      return openCount(page)
    }, { timeout: 700 }).toBe(2)
    expect(await statusesSeen(page)).not.toContain('reconnecting')
  })

  test('a hidden tab closes the stream and a visible one reopens it from the newest reading', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'live')

    await setVisibility(page, 'hidden')
    await expect(page.getByTestId('live-status')).toHaveAttribute('data-status', 'paused')
    await expect(page.getByTestId('live-status')).toHaveText('Paused')
    // Paused is a deliberate close: no reconnect is attempted while the tab stays hidden.
    await page.waitForTimeout(1500)
    expect(await streamOpens(page)).toHaveLength(1)

    await setVisibility(page, 'visible')
    await expect.poll(async () => {
      return openCount(page)
    }).toBe(2)
    expect((await streamOpens(page))[1]).toBe('?since=2026-11-07T10%3A00%3A04.000Z')
  })

  test('a silence in the readings is a gap in the line, never a straight line across it', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))
    // Steady cadence: one connected run, so one move command.
    await expect.poll(async () => {
      return lineRuns(page)
    }).toBe(1)

    // Ten minutes of silence (a hidden tab), then a reading: the line breaks between them.
    await pushFrame(page, 'metrics', reading(600))
    await expect.poll(async () => {
      return lineRuns(page)
    }).toBe(2)
  })

  test('the chart draws without animation under reduced motion', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))
    await expect(cpuCard(page)).toContainText('37.4%')

    await pushFrame(page, 'metrics', reading(8, { cpu: { total: 61.2, load: [1, 1, 1] } }))
    await expect(cpuCard(page)).toContainText('61.2%')
    const animations = await cpuCard(page).evaluate((card) => {
      return card.getAnimations({ subtree: true }).length
    })
    expect(animations).toBe(0)
  })

  for (const locale of ['en', 'ru', 'hy'] as const) {
    test(`at 390 px nothing scrolls sideways while live, in ${locale}`, async ({ page }) => {
      await openLiveDashboard(page, { locale, width: 390 })
      await goLive(page)
      await pushFrame(page, 'metrics', reading(6))
      await expect(page.getByTestId('metric-card-cpu')).toBeVisible()

      const widths = await page.evaluate(() => {
        const main = document.querySelector('main')
        return {
          page: document.documentElement.scrollWidth - window.innerWidth,
          panel: main === null ? 0 : main.scrollWidth - main.clientWidth,
        }
      })
      expect(widths.page).toBeLessThanOrEqual(0)
      expect(widths.panel).toBeLessThanOrEqual(0)
    })
  }
})
