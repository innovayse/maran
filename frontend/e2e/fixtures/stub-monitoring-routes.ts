import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { LogLevel } from '../../src/types/logLevel'
import type {
  AccountDiskUsage,
  ChartRange,
  ClockSync,
  HostMetrics,
  MetricBucket,
  PendingUpdate,
  ServiceStatus,
  UpdateClassificationChoice,
} from '../../src/types/monitoring'

/** The chart endpoint. The query part is optional in the pattern so a missing `range` still matches. */
const CHART_PATTERN = /\/api\/v1\/monitoring\/chart(\?.*)?$/

/** The host's live resource reading. */
const METRICS_PATTERN = /\/api\/v1\/monitoring\/metrics$/

/** The host clock's synchronisation. */
const CLOCK_PATTERN = /\/api\/v1\/monitoring\/clock$/

/** The watched services' states. */
const SERVICES_PATTERN = /\/api\/v1\/monitoring\/services$/

/** The panel's runtime log level (issue #131). */
const LOG_LEVEL_PATTERN = /\/api\/v1\/log-level$/

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

/**
 * The log level a stubbed monitoring screen reports unless a spec passes its own: Information, with
 * the six levels the panel offers named as its English resources name them.
 */
export const STUBBED_LOG_LEVEL: LogLevel = {
  level: 'information',
  levelDisplayName: 'Information — the normal level',
  choices: [
    { level: 'verbose', levelDisplayName: 'Verbose — everything' },
    { level: 'debug', levelDisplayName: 'Debug — diagnostic detail' },
    { level: 'information', levelDisplayName: 'Information — the normal level' },
    { level: 'warning', levelDisplayName: 'Warning — only what needs attention' },
    { level: 'error', levelDisplayName: 'Error — only failures' },
    { level: 'fatal', levelDisplayName: 'Fatal — only failures that stop the panel' },
  ],
}

/** Every level the stub was asked to apply, in order, as the raw `level` each change carried. */
export const logLevelChanges: string[] = []

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
 * Fulfils the four monitoring reads and the log level card's read and change.
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
 * @param logLevel The log level reported first; {@link STUBBED_LOG_LEVEL} by default. A change the
 * stub accepts becomes the level it reports afterwards, as the panel's read-back does.
 * @returns Resolves once the routes are installed.
 */
export const stubMonitoring = async (
  page: Page,
  buckets: Record<ChartRange, MetricBucket[]>,
  services: ServiceStatus[],
  accounts: AccountDiskUsage[],
  metrics: HostMetrics = STUBBED_HOST_METRICS,
  clock: ClockSync = STUBBED_CLOCK_SYNC,
  logLevel: LogLevel = STUBBED_LOG_LEVEL,
): Promise<void> => {
  let current = logLevel.level
  await page.route(LOG_LEVEL_PATTERN, async (route: Route) => {
    if (route.request().method() === 'PUT') {
      const requested = (route.request().postDataJSON() as { level: LogLevel['level'] }).level
      logLevelChanges.push(requested)
      current = requested
      await route.fulfill({ status: 204 })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ...logLevel,
        level: current,
        levelDisplayName:
          logLevel.choices.find((choice) => {
            return choice.level === current
          })?.levelDisplayName ?? current,
      }),
    })
  })

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

/** The pending updates list, with or without the list's query. */
const UPDATES_PATTERN = /\/api\/v1\/monitoring\/updates(\?.*)?$/

/** The number of pending updates, read for the navigation badge. */
const UPDATES_COUNT_PATTERN = /\/api\/v1\/monitoring\/updates\/count$/

/** Whether the server needs a reboot to finish applying updates. */
const REBOOT_REQUIRED_PATTERN = /\/api\/v1\/monitoring\/updates\/reboot-required$/

/** The kinds list the filter sheet, the chip and the rows' names read. */
const CLASSIFICATIONS_PATTERN = /\/api\/v1\/monitoring\/updates\/classifications$/

/** The order a `security` sort gives the classifications: security updates first. */
const CLASSIFICATION_ORDER: Readonly<Record<string, string>> = {
  security: '0',
  ordinary: '1',
  unmeasured: '2',
}

/** What each shipped language calls the three kinds, as the backend's resource files do; order matches {@link CLASSIFICATION_ORDER}. */
const CLASSIFICATION_NAMES: Readonly<Record<string, readonly [string, string, string]>> = {
  en: ['Security', 'Ordinary', 'Not measured'],
  ru: ['Безопасность', 'Обычное', 'Не проверено'],
  hy: ['Անվտանգություն', 'Սովորական', 'Չստուգված'],
}

/** A pending update as a spec writes it: the stub names its kind in the language each request asks for. */
export type StubbedPendingUpdate = Omit<PendingUpdate, 'classificationName'>

/**
 * Names the three kinds in the language a request asks for, falling back to English the way the
 * panel does.
 * @param acceptLanguage The request's `Accept-Language` header, or an empty string.
 * @returns The kinds in the panel's order, each with its name.
 */
const classificationsFor = (acceptLanguage: string): UpdateClassificationChoice[] => {
  const language = Object.keys(CLASSIFICATION_NAMES).find((candidate) => {
    return acceptLanguage.toLowerCase().startsWith(candidate)
  })
  const [security, ordinary, unmeasured] = CLASSIFICATION_NAMES[language ?? 'en']
  return [
    { value: 'security', name: security },
    { value: 'ordinary', name: ordinary },
    { value: 'unmeasured', name: unmeasured },
  ]
}

/** What the stub answers on the reboot-required route: a measured answer, an unmeasured one, or a 500. */
export type StubbedReboot = boolean | null | 'failure'

/** What a spec may say about the notices above the list; every field defaults to the quiet answer. */
export interface StubbedUpdateNotices {
  /** The count route's `securityCount`; `null` (the default) is "not measured". */
  securityCount?: number | null
  /** The reboot-required route's answer: `true`, `false`, `null` (not measured) or `'failure'` (a 500). */
  reboot?: StubbedReboot
}

/**
 * How the pending updates list searches and sorts, as the panel does: the search reads the package
 * name, and the sorts are the two the panel whitelists, `packageName` and `source`.
 */
const UPDATES_RULES: PagedRules<StubbedPendingUpdate> = {
  matches: (row, term) => {
    return row.packageName.toLowerCase().includes(term)
  },
  sorts: {
    packageName: (row) => {
      return row.packageName
    },
    source: (row) => {
      return row.source
    },
    security: (row) => {
      return CLASSIFICATION_ORDER[row.classification]
    },
  },
  filters: {
    classification: (row, value) => {
      return row.classification === value
    },
  },
}

/** Every pending updates list request the stub answered, in order, so a spec can read the query. */
export const pendingUpdateRequests: URL[] = []

/**
 * Fulfils the pending updates list and its count.
 *
 * A stub given `failure` answers both routes with a 500 problem body carrying that message, the way
 * the panel fails a request when the agent could not read the package cache: the typed failure, never
 * an empty page and never a zero. The classification filter and the `security` sort are honoured as
 * the panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param updates Every update the stub knows, each given by its wire kind; the stub adds the kind's name in the request's language and answers them a page at a time, honouring `page`,
 * `pageSize`, `search` and `sort` as the panel does.
 * @param failure The problem `detail` to answer both routes with instead of the data, or `null`.
 * @param notices The security count and the reboot-required answer; both default to "not measured".
 * @returns Resolves once the routes are installed.
 */
export const stubPendingUpdates = async (
  page: Page,
  updates: StubbedPendingUpdate[],
  failure: string | null = null,
  notices: StubbedUpdateNotices = {},
): Promise<void> => {
  pendingUpdateRequests.length = 0
  const reboot = notices.reboot ?? null

  /**
   * Answers a request with the stubbed failure.
   * @param route The intercepted request.
   * @param detail The problem detail to carry.
   * @returns Resolves once the response is sent.
   */
  const fail = async (route: Route, detail: string): Promise<void> => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'The package cache could not be read', detail, status: 500 }),
    })
  }

  await page.route(UPDATES_COUNT_PATTERN, async (route: Route) => {
    if (failure !== null) {
      await fail(route, failure)
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ count: updates.length, securityCount: notices.securityCount ?? null }),
    })
  })

  await page.route(REBOOT_REQUIRED_PATTERN, async (route: Route) => {
    if (reboot === 'failure') {
      await fail(route, 'The reboot requirement could not be read.')
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ required: reboot }) })
  })

  await page.route(CLASSIFICATIONS_PATTERN, async (route: Route) => {
    const language = route.request().headers()['accept-language'] ?? ''
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(classificationsFor(language)) })
  })

  await page.route(UPDATES_PATTERN, async (route: Route) => {
    pendingUpdateRequests.push(new URL(route.request().url()))
    if (failure !== null) {
      await fail(route, failure)
      return
    }
    // The panel names each row's kind in the language the request carries.
    const names = classificationsFor(route.request().headers()['accept-language'] ?? '')
    const answer = pagedAnswer(updates, route.request().url(), UPDATES_RULES)
    const items: PendingUpdate[] = answer.items.map((row) => {
      const named = names.find((choice) => {
        return choice.value === row.classification
      })
      return { ...row, classificationName: named?.name ?? row.classification }
    })
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...answer, items }),
    })
  })
}
