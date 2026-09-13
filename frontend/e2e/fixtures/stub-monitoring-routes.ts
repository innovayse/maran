import type { Page, Route } from '@playwright/test'
import type { AccountDiskUsage, ChartRange, MetricBucket, ServiceStatus } from '../../src/types/monitoring'

/** The chart endpoint. The query part is optional in the pattern so a missing `range` still matches. */
const CHART_PATTERN = /\/api\/v1\/monitoring\/chart(\?.*)?$/

/** The watched services' states. */
const SERVICES_PATTERN = /\/api\/v1\/monitoring\/services$/

/** The per-account disk figures. */
const ACCOUNTS_DISK_PATTERN = /\/api\/v1\/monitoring\/accounts-disk$/

/** A fixed UTC instant the stubbed buckets are spaced from, so nothing depends on the clock. */
const BASE_AT = Date.UTC(2026, 8, 1, 0, 0, 0)

/** Five minutes, the bucket width the panel uses for the day range. */
const FIVE_MINUTES_MS = 5 * 60 * 1000

/**
 * Every chart request the stub answered, in order, as the raw `range` value each carried.
 *
 * Recorded rather than asserted through a `waitForRequest`: the proposition is that switching the
 * toggle asks the panel a DIFFERENT question, and the evidence for that is the sequence of values
 * the panel was asked for.
 */
export const chartRangeRequests: string[] = []

/**
 * Builds a bucket whose every metric is derived from one number, so a spec can say what it expects
 * to read off the chart without spelling out nine fields.
 *
 * The first bucket carries `null` network rates, exactly as the panel's own first bucket does: a
 * rate is the difference between two counter readings over the seconds between them, and the first
 * has no earlier reading to measure against.
 * @param index Which bucket this is, counting from zero; also its offset from {@link BASE_AT}.
 * @param cpuPercent The processor reading this bucket carries.
 * @returns One bucket in the shape `MetricBucketDto` serializes to.
 */
export const stubbedBucket = (index: number, cpuPercent: number): MetricBucket => {
  return {
    at: new Date(BASE_AT + index * FIVE_MINUTES_MS).toISOString(),
    cpuPercent,
    memoryUsedBytes: 2 * 1024 * 1024 * 1024,
    memoryTotalBytes: 8 * 1024 * 1024 * 1024,
    diskUsedBytes: 40 * 1024 * 1024 * 1024,
    diskTotalBytes: 100 * 1024 * 1024 * 1024,
    loadAverage1m: 0.5,
    networkReceivedBytesPerSecond: index === 0 ? null : 1024 * 1024,
    networkSentBytesPerSecond: index === 0 ? null : 512 * 1024,
  }
}

/**
 * Fulfils the three monitoring reads.
 *
 * **The chart stub echoes back the range it was ASKED for**, which is what the module does
 * (`MetricsChartDto.Range`) and what the store checks before accepting an answer. A stub that
 * always echoed `lastDay` would make the seven-day answer look stale and the screen would discard
 * it — the stub agreeing with the SPA instead of with the module is exactly the failure mode a
 * network-stubbed spec is prone to, so the echo is taken from the request.
 * @param page The Playwright page whose network the routes are installed on.
 * @param buckets Which buckets the chart reports, keyed by range.
 * @param services The service rows reported.
 * @param accounts The per-account disk rows reported.
 * @returns Resolves once the routes are installed.
 */
export const stubMonitoring = async (
  page: Page,
  buckets: Record<ChartRange, MetricBucket[]>,
  services: ServiceStatus[],
  accounts: AccountDiskUsage[],
): Promise<void> => {
  await page.route(CHART_PATTERN, async (route: Route) => {
    const requested = new URL(route.request().url()).searchParams.get('range') ?? ''
    chartRangeRequests.push(requested)

    // Only the two values the panel's validator accepts are answered; anything else is refused the
    // way the module refuses it, so a spec cannot pass on a range the panel would have rejected.
    if (requested !== 'lastDay' && requested !== 'lastWeek') {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: 'Validation failed', detail: 'Range is not one of the two offered.' }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        range: requested,
        bucketSeconds: requested === 'lastWeek' ? 1800 : 300,
        buckets: buckets[requested],
      }),
    })
  })

  await page.route(SERVICES_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(services) })
  })

  await page.route(ACCOUNTS_DISK_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(accounts) })
  })
}
