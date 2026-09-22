import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { chartRangeRequests, stubbedBucket, stubMonitoring } from '../fixtures/stub-monitoring-routes'
import type { AccountDiskUsage, ChartRange, MetricBucket, ServiceStatus } from '../../src/types/monitoring'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
]

// Five day-range buckets. The processor values are distinct and deliberately not round, so a
// readout showing one of them cannot be a coincidence of the chart's own default formatting.
const DAY_BUCKETS: MetricBucket[] = [
  stubbedBucket(0, 11.5),
  stubbedBucket(1, 22.5),
  stubbedBucket(2, 73.5),
  stubbedBucket(3, 34.5),
  stubbedBucket(4, 45.5),
]

// A different count and different values from the day range, so a screen that never re-read the
// panel cannot pass the toggle spec by showing what it already had.
const WEEK_BUCKETS: MetricBucket[] = [stubbedBucket(0, 91.5), stubbedBucket(1, 92.5)]

const BUCKETS: Record<ChartRange, MetricBucket[]> = { lastDay: DAY_BUCKETS, lastWeek: WEEK_BUCKETS }

// Three rows, one per state the agent reports — including the not-known one, which exists because
// a socket-activated SSH unit is inactive from boot until the first connection. The `name` is the
// backend-localized label the panel now ships beside the machine member (English here, as the
// suite's stubbed backend answers an English interface).
const SERVICES: ServiceStatus[] = [
  { service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' },
  { service: 'database', name: 'Database', state: 'stopped', detail: 'inactive (dead)' },
  { service: 'ssh', name: 'SSH', state: 'unknown', detail: 'socket-activated' },
]

// Three accounts: one measured under its allowance, one measured over it, and one the agent did
// not report at all — the case the DTO's `usedBytes` is nullable for.
const ACCOUNTS: AccountDiskUsage[] = [
  {
    accountId: '11111111-1111-1111-1111-111111111111',
    username: 'alice',
    usedBytes: 512 * 1024 * 1024,
    quotaBytes: 1024 * 1024 * 1024,
    enforceable: true,
    note: null,
  },
  {
    accountId: '22222222-2222-2222-2222-222222222222',
    username: 'bob',
    usedBytes: 3 * 1024 * 1024 * 1024,
    quotaBytes: 2 * 1024 * 1024 * 1024,
    enforceable: true,
    note: null,
  },
  {
    accountId: '33333333-3333-3333-3333-333333333333',
    username: 'carol',
    usedBytes: null,
    quotaBytes: 1024 * 1024 * 1024,
    enforceable: true,
    note: null,
  },
  {
    accountId: '44444444-4444-4444-4444-444444444444',
    username: 'dave',
    usedBytes: 256 * 1024 * 1024,
    quotaBytes: 1024 * 1024 * 1024,
    enforceable: false,
    note: 'Not currently enforced — mount the filesystem with quota accounting and run quotaon.',
  },
]

/**
 * Opens the monitoring screen as a signed-in administrator with the three reads stubbed.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen has finished its first load.
 */
const openMonitoring = async (page: Page): Promise<void> => {
  chartRangeRequests.length = 0
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMonitoring(page, BUCKETS, SERVICES, ACCOUNTS)
  await page.goto('/monitoring')
  // A generous timeout for the FIRST paint only: on the dev server a cold module graph occasionally
  // takes longer than the suite's 5 s default, and a slow compile is not the proposition under test.
  // Every assertion that follows keeps the default.
  await expect(page.getByTestId('monitoring-charts')).toBeVisible({ timeout: 20_000 })
}

// The proposition: the stubbed buckets reach the plot, and the value the header reads back is the
// LAST bucket's, formatted with the unit the page chose. Breakable: a page that dropped the series
// on the way to `UiChart` renders six empty states and no "45.5 %" anywhere; one that plotted the
// buckets in the wrong order reads back 11.5; one that lost the unit reads back a bare number.
test('the processor chart plots the stubbed buckets and reads back the newest one', async ({ page }) => {
  await openMonitoring(page)

  const cpu = page.getByTestId('monitoring-charts').locator('.ui-chart').first()

  // The header reading, which `UiChart` takes from the LAST point of the series.
  await expect(cpu.locator('span.font-mono')).toHaveText('45.5 %')

  // The `sr-only` table `UiChart` renders beside its aria-hidden plot: every bucket, as real cells.
  // Asserting on it proves the whole series arrived, not merely that something was drawn. Matched
  // as cells rather than as text, because the header reading is the same string as the last cell —
  // a bare text match is ambiguous by construction here, not flaky.
  await expect(cpu.getByRole('cell', { name: '11.5 %', exact: true })).toHaveCount(1)
  await expect(cpu.getByRole('cell', { name: '73.5 %', exact: true })).toHaveCount(1)
  // Five buckets plus the header row: a series that arrived truncated fails here even if the
  // individual values above are present.
  await expect(cpu.getByRole('row')).toHaveCount(6)
})

// The proposition: hovering the plot produces a readout formatted through the page's own
// `formatValue` — two decimals for the load average, which the chart's one-decimal default would
// render as "0.5" — and NOTHING after the number, because a load average is dimensionless. The
// readout used to append a word ("0.50 load", "0.50 нагрузка" in Russian): a unit invented for a
// number that has none. Breakable: drop the `format-value` prop and the readout reads "0.5";
// reintroduce a `unit` on the load chart and the exact-match below fails on the extra token;
// break the pointer handling and no readout appears at all.
test('hovering the load chart shows a bare two-decimal readout, with no invented unit', async ({ page }) => {
  await openMonitoring(page)

  const loadChart = page.getByTestId('monitoring-charts').locator('.ui-chart').nth(5)
  await loadChart.locator('.ui-chart-svg').hover()

  // The readout drawn inside the plot, not the header reading and not a table cell — only the
  // hover path produces this element at all. `toHaveText` matches the FULL text, so a readout of
  // "0.50 load" (the shipped defect) fails here — the assertion is the value, not a substring.
  await expect(loadChart.locator('.ui-chart-readout-value')).toHaveText('0.50')
})

// The proposition: the 7 d segment causes a NEW request carrying `range=lastWeek`, and the screen
// shows what that request answered. Breakable in three ways, each of which this catches: a toggle
// that only re-sliced what was held sends one request and the recorded list stays `['lastDay']`; a
// toggle that refetched without the parameter records `''` and the stub answers 400; a screen that
// discarded the answer keeps reading back 45.5 % from the day range.
test('the 7 d toggle refetches the chart with the week range', async ({ page }) => {
  await openMonitoring(page)
  expect(chartRangeRequests).toEqual(['lastDay'])

  await page.getByRole('button', { name: '7 d' }).click()

  const cpu = page.getByTestId('monitoring-charts').locator('.ui-chart').first()
  await expect(cpu.locator('span.font-mono')).toHaveText('92.5 %')
  // Two buckets plus the header row: the week stub answers a different COUNT from the day stub, so
  // a screen that kept the day's points cannot pass this even if it re-read the panel.
  await expect(cpu.getByRole('row')).toHaveCount(3)
  expect(chartRangeRequests).toEqual(['lastDay', 'lastWeek'])
})

// The proposition: each account's row carries a ratio bar whose announced value names both figures,
// and the bar's ARIA bounds are the account's own. Breakable: remove `UiMeter` and the progressbar
// role disappears; hardcode the bar's width instead of the ratio and `aria-valuenow` stops matching
// the used figure; feed it megabytes against bytes and the announced sentence disagrees with the
// cells beside it.
test('the per-account disk table draws a ratio bar naming used against allowance', async ({ page }) => {
  await openMonitoring(page)

  const alice = page.getByTestId('account-disk-row').filter({ hasText: 'alice' })
  const bar = alice.getByRole('progressbar')
  await expect(bar).toHaveAttribute('aria-valuetext', '512 MiB of 1 GiB')
  await expect(bar).toHaveAttribute('aria-valuenow', String(512 * 1024 * 1024))
  await expect(bar).toHaveAttribute('aria-valuemax', String(1024 * 1024 * 1024))
})

// The proposition: an account the agent did not measure gets NO bar. This is the defect a reviewer
// caught on the server (a `0L` where a null belonged): an empty bar is a picture of "using
// nothing", which is the one claim the nullable field exists to avoid making. Breakable by the
// obvious fix that looks harmless — `:value="row.usedBytes ?? 0"` — which puts a zero-width
// progressbar in the row and fails the count assertion below.
test('an account the agent did not measure gets no bar and says so', async ({ page }) => {
  await openMonitoring(page)

  const carol = page.getByTestId('account-disk-row').filter({ hasText: 'carol' })
  await expect(carol.getByRole('progressbar')).toHaveCount(0)
  await expect(carol.getByText('Not measured')).toHaveCount(2)
})

// The proposition: an account whose quota this server cannot enforce gets NO progress bar against
// its allowance — a bar there would draw a limit as if it were in force, which is issue #29's own
// defect. It gets a "not enforced" badge instead, and the figure it can back (the used bytes) is
// still shown in plain text. Breakable: draw `UiMeter` unconditionally and the progressbar count
// below stops being zero for this row; drop the badge and the text assertion fails.
test('an account whose quota cannot be enforced gets no bar and a badge instead', async ({ page }) => {
  await openMonitoring(page)

  const dave = page.getByTestId('account-disk-row').filter({ hasText: 'dave' })
  await expect(dave.getByRole('progressbar')).toHaveCount(0)
  await expect(dave.getByText('Not enforced', { exact: true })).toBeVisible()
  await expect(dave.getByText('256 MiB, allowance not enforced')).toBeVisible()
})

// The proposition: all three states the agent reports are rendered, each with its own text — the
// badge never carries its meaning in colour alone, and "not known" is never collapsed into
// "stopped". Breakable: map `unknown` onto the stopped label and the third badge reads "Stopped",
// so the "Not known" assertion fails.
test('every service state the agent reports is shown with its own wording', async ({ page }) => {
  await openMonitoring(page)

  const services = page.getByTestId('monitoring-services')
  await expect(services.getByText('Running')).toBeVisible()
  await expect(services.getByText('Stopped')).toBeVisible()
  await expect(services.getByText('Not known')).toBeVisible()
})
