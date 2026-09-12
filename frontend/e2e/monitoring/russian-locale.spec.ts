import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubbedBucket, stubMonitoring } from '../fixtures/stub-monitoring-routes'
import type { AccountDiskUsage, ChartRange, MetricBucket, ServiceStatus } from '../../src/types/monitoring'
import type { PanelModule } from '../../src/types/module'

// Four findings on this screen were measured in a live browser WITH THE INTERFACE IN RUSSIAN, and
// every one of them was invisible in English: machine constants beside Russian words, an
// English-only chart axis, an invented unit that happened to read naturally in English ("load"),
// and a Cyrillic byte unit beside an IEC one. So this file asserts the VALUES a Russian screen
// renders (rules/testing.md: assert the value, in a non-English locale), against stubs shaped
// exactly as the panel answers a `ru` request.

// The axis and readout print HH:mm through the browser's clock; pinned so the asserted strings are
// the same on every machine, the same pin the ui-kit chart spec uses.
test.use({ timezoneId: 'UTC' })

const LICENSED: PanelModule[] = [
  { name: 'monitoring', displayName: 'Мониторинг', tier: 'included', isEnabled: true },
]

// The buckets sit on 1 September 2026 UTC (the stub's own BASE_AT), so the Russian axis form of
// that instant — "1 сент., 00:00" — is a fixed string this file can assert byte-for-byte.
const DAY_BUCKETS: MetricBucket[] = [stubbedBucket(0, 11.5), stubbedBucket(1, 22.5), stubbedBucket(2, 45.5)]

const BUCKETS: Record<ChartRange, MetricBucket[]> = { lastDay: DAY_BUCKETS, lastWeek: DAY_BUCKETS }

// The names are the backend's own `ru` DisplayNames values (`ServiceDisplayNames`): the stub plays
// a panel answering an `Accept-Language: ru` request. `ServiceDisplayNamesTests` on the backend is
// what pins these very strings to the real resx; here they prove the SPA renders what arrives.
const SERVICES: ServiceStatus[] = [
  { service: 'webServer', name: 'Веб-сервер', state: 'running', detail: 'active (running)' },
  { service: 'database', name: 'База данных', state: 'stopped', detail: 'inactive (dead)' },
  { service: 'ssh', name: 'SSH', state: 'unknown', detail: 'socket-activated' },
]

const ACCOUNTS: AccountDiskUsage[] = [
  {
    accountId: '11111111-1111-1111-1111-111111111111',
    username: 'alice',
    usedBytes: 512 * 1024 * 1024,
    quotaBytes: 1024 * 1024 * 1024,
  },
]

/**
 * Opens the monitoring screen as a signed-in administrator with a persisted Russian interface.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen has finished its first load.
 */
const openRussianMonitoring = async (page: Page): Promise<void> => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMonitoring(page, BUCKETS, SERVICES, ACCOUNTS)
  await page.goto('/monitoring')
  // First-paint allowance only, as monitoring.spec.ts explains; later assertions keep the default.
  await expect(page.getByTestId('monitoring-charts')).toBeVisible({ timeout: 20_000 })
}

// The measured defect: the services card printed `webServer`, `database`, `cron`, `ssh` — machine
// constants — beside localized status badges. The fix ships a backend-localized `name` on each row
// and the card renders it. Breakable: render `status.service` again and "Веб-сервер" disappears
// from the card while `webServer` reappears, failing both halves below.
test('the services card names services in Russian, never by their machine constant', async ({ page }) => {
  await openRussianMonitoring(page)

  const services = page.getByTestId('monitoring-services')

  // The VALUE the backend localized, not "some text": both real words and their exact spelling.
  await expect(services.getByText('Веб-сервер')).toBeVisible()
  await expect(services.getByText('База данных')).toBeVisible()

  // Positive control on the probe: this same case-insensitive text probe DOES find a
  // machine-token-shaped string when one is rendered — the SSH row's name. Silence from the
  // `webServer` probe below is therefore the constant's absence, not the probe's blindness.
  await expect(services.getByText('ssh')).toBeVisible()

  // The machine constants themselves, absent. `getByText` matches substrings case-insensitively,
  // so these also refuse `webserver`, `WebServer` and friends.
  await expect(services.getByText('webServer')).toHaveCount(0)
  await expect(services.getByText('database')).toHaveCount(0)
})

// The measured defect: the chart axis read `8 Sep, 17:35` — date-fns' own English — while tables
// on the same screen read `8 сент. 2026` through the locale-mapped formatters. The fix routes the
// chart through the same mapping (`formatChartInstant`). Breakable: drop the locale option from
// the chart's formatter and both surfaces below read "1 Sep, 00:00" again.
test('the chart axis and readings table speak Russian on a Russian screen', async ({ page }) => {
  await openRussianMonitoring(page)

  const cpu = page.getByTestId('monitoring-charts').locator('.ui-chart').first()

  // The visible axis: its first tick is the first bucket, as the exact Russian string.
  await expect(cpu.locator('.ui-chart-axis-label').filter({ hasText: '1 сент., 00:00' })).toHaveCount(1)

  // The same instant in the chart's readings table — same formatter by construction, asserted so
  // the sr-only surface a screen reader gets is proven Russian too, as a real cell.
  await expect(cpu.getByRole('cell', { name: '1 сент., 00:00', exact: true })).toHaveCount(1)
})

// The measured defect: the load-average card read `2.10 нагрузка` under a caption ending
// `в нагрузка` — ungrammatical, and wrong before grammar: a load average is dimensionless, so no
// unit word belongs after it in any language. Breakable: hand the load chart a `unit` again and
// the exact-match header grows a token; restore the one-caption-with-a-hole and the caption grows
// the dangling `в` back.
test('the load average reads as a bare number under a grammatical Russian caption', async ({ page }) => {
  await openRussianMonitoring(page)

  const load = page.getByTestId('monitoring-charts').locator('.ui-chart').nth(5)

  // The header reading: the newest bucket's value and NOTHING else.
  await expect(load.locator('span.font-mono')).toHaveText('0.50')

  // The readings table's caption: a complete Russian sentence with no unit slot at all.
  await expect(load.locator('caption')).toHaveText('Показания Средняя нагрузка, 1 мин')
})

// The measured defect: two byte formatters on one screen — the disk table's `4.7 KiB` / `25 GiB`
// (utils/formatBytes, IEC symbols by design) beside the memory card's `6.7 ГиБ` (a translated
// bundle key). The fix keeps formatBytes as the one authority, so a Russian screen reads the same
// IEC symbol everywhere. Breakable: point the memory chart back at a bundle key and its header
// reads `2 ГиБ`, failing the exact match and the ГиБ-absence probe together.
test('byte units are the same IEC symbols in the charts as in the disk table', async ({ page }) => {
  await openRussianMonitoring(page)

  const charts = page.getByTestId('monitoring-charts')

  // The memory chart's header: the stubbed 2 GiB, with the IEC symbol — the disk table's spelling.
  await expect(charts.locator('.ui-chart').nth(1).locator('span.font-mono')).toHaveText('2 GiB')

  // The disk table beside it, formatted by formatBytes: one spelling on one screen.
  await expect(page.getByTestId('account-disk-row').getByText('512 MiB')).toBeVisible()

  // Positive control on the probe: the same text probe finds the surviving symbol...
  await expect(charts.getByText('GiB').first()).toBeVisible()

  // ...so silence on the Cyrillic twin is its absence, not the probe's.
  await expect(charts.getByText('ГиБ')).toHaveCount(0)
})
