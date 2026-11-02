import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type {
  AccountDiskUsage,
  ChartRange,
  ClockSync,
  HostMetrics,
  MetricBucket,
  ServiceStatus,
} from '../../src/types/monitoring'

/** The chart endpoint. The query part is optional in the pattern so a missing `range` still matches. */
const CHART_PATTERN = /\/api\/v1\/monitoring\/chart(\?.*)?$/

/** The host's live resource reading. */
const METRICS_PATTERN = /\/api\/v1\/monitoring\/metrics$/

/** The host clock's synchronisation. */
const CLOCK_PATTERN = /\/api\/v1\/monitoring\/clock$/

/** The watched services' states. */
const SERVICES_PATTERN = /\/api\/v1\/monitoring\/services$/

/** The per-account disk figures, with or without the list's query. */
const ACCOUNTS_DISK_PATTERN = /\/api\/v1\/monitoring\/accounts-disk(\?.*)?$/

/**
 * How the disk list searches and sorts, as the panel does: the search reads the user name, and the
 * sorts are the two the panel whitelists — never used bytes, which it cannot order by because it
 * measures only the page it answers.
 */
const ACCOUNTS_DISK_RULES: PagedRules<AccountDiskUsage> = {
  matches: (row, term) => {
    return row.username.toLowerCase().includes(term)
  },
  sorts: {
    username: (row) => {
      return row.username
    },
    quotaBytes: (row) => {
      // Zero-padded so the stub's text comparison orders the allowances as numbers.
      return String(row.quotaBytes).padStart(20, '0')
    },
  },
  filters: {},
}

/** One gibibyte, the unit the stubbed byte figures are written in. */
const GIB = 1024 * 1024 * 1024

/**
 * The live reading a stubbed monitoring screen shows unless a spec passes its own: an ext4 root of
 * 98 GiB holding 40 GiB, with a 5 GiB root reserve and 53 GiB available — three figures that sum
 * to the size, as the panel's are (issue #77).
 */
export const STUBBED_HOST_METRICS: HostMetrics = {
  cpuPercent: 12.5,
  memoryUsedBytes: 2 * GIB,
  memoryTotalBytes: 8 * GIB,
  diskUsedBytes: 40 * GIB,
  diskReservedBytes: 5 * GIB,
  diskAvailableBytes: 53 * GIB,
  diskTotalBytes: 98 * GIB,
  networkRxBytes: 0,
  networkTxBytes: 0,
  loadAverage1m: 0.5,
  loadAverage5m: 0.4,
  loadAverage15m: 0.3,
}

/** A fixed UTC instant the stubbed buckets are spaced from, so nothing depends on the clock. */
const BASE_AT = Date.UTC(2026, 8, 1, 0, 0, 0)

/** Five minutes, the bucket width the panel uses for the day range. */
const FIVE_MINUTES_MS = 5 * 60 * 1000

/**
 * The clock reading a stubbed monitoring screen shows unless a spec passes its own: synchronised,
 * with the sentence the panel's English resources carry for it.
 */
export const STUBBED_CLOCK_SYNC: ClockSync = {
  state: 'synchronized',
  description: 'The clock is synchronised with a time source.',
}

/**
 * Every chart request the stub answered, in order, as the raw `range` value each carried.
 *
 * Recorded rather than asserted through a `waitForRequest`: the proposition is that switching the
 * toggle asks the panel a DIFFERENT question, and the evidence for that is the sequence of values
 * the panel was asked for.
 */
export const chartRangeRequests: string[] = []

/**
 * Every disk list request the stub answered, in order, so a spec can read which page, search and
 * sort the screen asked the panel for.
 */
export const accountDiskRequests: URL[] = []

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
    memoryUsedBytes: 2 * GIB,
    memoryTotalBytes: 8 * GIB,
    diskUsedBytes: 40 * GIB,
    diskTotalBytes: 100 * GIB,
    loadAverage1m: 0.5,
    networkReceivedBytesPerSecond: index === 0 ? null : 1024 * 1024,
    networkSentBytesPerSecond: index === 0 ? null : 512 * 1024,
  }
}

/**
 * Fulfils the four monitoring reads.
 *
 * **The chart stub echoes back the range it was ASKED for**, which is what the module does
 * (`MetricsChartDto.Range`) and what the store checks before accepting an answer. A stub that
 * always echoed `lastDay` would make the seven-day answer look stale and the screen would discard
 * it — the stub agreeing with the SPA instead of with the module is exactly the failure mode a
 * network-stubbed spec is prone to, so the echo is taken from the request.
 * @param page The Playwright page whose network the routes are installed on.
 * @param buckets Which buckets the chart reports, keyed by range.
 * @param services The service rows reported.
 * @param accounts Every per-account disk row the stub knows; it answers them a page at a time,
 * honouring `page`, `pageSize`, `search` and `sort` as the panel does.
 * @param metrics The live host reading reported; {@link STUBBED_HOST_METRICS} by default.
 * @param clock The clock reading reported; {@link STUBBED_CLOCK_SYNC} by default.
 * @returns Resolves once the routes are installed.
 */
export const stubMonitoring = async (
  page: Page,
  buckets: Record<ChartRange, MetricBucket[]>,
  services: ServiceStatus[],
  accounts: AccountDiskUsage[],
  metrics: HostMetrics = STUBBED_HOST_METRICS,
  clock: ClockSync = STUBBED_CLOCK_SYNC,
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

  await page.route(METRICS_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(metrics) })
  })

  await page.route(CLOCK_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(clock) })
  })

  await page.route(SERVICES_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(services) })
  })

  await page.route(ACCOUNTS_DISK_PATTERN, async (route: Route) => {
    accountDiskRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(accounts, route.request().url(), ACCOUNTS_DISK_RULES)),
    })
  })
}
