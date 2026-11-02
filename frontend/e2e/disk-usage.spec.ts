import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from './fixtures/stub-auth-routes'
import { loaded, stubDashboard, SYNCHRONIZED_CLOCK } from './fixtures/stub-dashboard-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubModules } from './fixtures/stub-modules-route'
import { stubMonitoring } from './fixtures/stub-monitoring-routes'
import { formatBytes } from '../src/utils/formatBytes'
import type { Dashboard } from '../src/types/dashboard'
import type { PanelModule } from '../src/types/module'
import type { HostMetrics } from '../src/types/monitoring'

// Issue #77: the root filesystem is reported as three figures that sum to its size — used (df's
// Used), reserved for the system (the root reserve, 0 on xfs) and available (df's Avail). The panel
// once showed size minus available as "in use", 12.7 GiB where df said 7.7. These specs hold the
// three labelled figures on both screens that draw the disk, and the sum they must make.

const LICENSED: PanelModule[] = [{ name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true }]

/** The figures the issue measured on an ext4 root: 97.9 GiB, 7.7 used, ~4.9 reserved for root. */
const EXT4: HostMetrics = {
  cpuPercent: 12.5,
  memoryUsedBytes: 2 * 1024 ** 3,
  memoryTotalBytes: 8 * 1024 ** 3,
  diskUsedBytes: 8_267_812_864,
  diskReservedBytes: 5_255_966_208,
  diskAvailableBytes: 91_595_545_088,
  diskTotalBytes: 105_119_324_160,
  networkRxBytes: 0,
  networkTxBytes: 0,
  loadAverage1m: 0.5,
  loadAverage5m: 0.4,
  loadAverage15m: 0.3,
}

/** The same disk on xfs, which keeps no root reserve: used plus available is the whole size. */
const XFS: HostMetrics = {
  ...EXT4,
  diskReservedBytes: 0,
  diskAvailableBytes: 96_851_511_296,
}

/**
 * The landing screen around the reading: every other section answered and empty, so the disk tile
 * is the only place any of these figures can come from.
 * @param metrics The live reading the resources section carries.
 * @returns The dashboard payload.
 */
const dashboardWith = (metrics: HostMetrics): Dashboard => {
  return {
    isAdministrator: true,
    resources: loaded(metrics),
    services: loaded([]),
    counts: { accounts: loaded(0), sites: loaded(0), databases: loaded(0) },
    attention: {
      certificatesExpiringSoon: loaded(0),
      failedBackups: loaded(0),
      bannedAddresses: loaded(0),
      failedTasks: loaded(0),
      clock: SYNCHRONIZED_CLOCK,
    },
    recentAudit: loaded([]),
  }
}

/**
 * Signs an administrator in with the dashboard and the monitoring reads answering `metrics`, and
 * opens the landing screen.
 * @param page The Playwright page.
 * @param metrics The live reading both screens are answered with.
 * @returns Resolves once the landing screen is open.
 */
const openLanding = async (page: Page, metrics: HostMetrics): Promise<void> => {
  await stubModules(page, LICENSED)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubDashboard(page, dashboardWith(metrics))
  await stubMonitoring(page, { lastDay: [], lastWeek: [] }, [], [], metrics)
  await page.goto('/')
}

/**
 * Moves from the landing screen to the monitoring screen the way an operator does: by the sidebar.
 * @param page The Playwright page.
 * @returns The monitoring screen's disk card.
 */
const clickToMonitoring = async (page: Page): Promise<Locator> => {
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Monitoring', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Monitoring', level: 1 })).toBeVisible({ timeout: 20_000 })
  return page.getByTestId('host-disk-card')
}

/**
 * Asserts the three labelled figures in the disk legend inside `scope`, and that the bar's
 * accessible name states all of them with the size.
 * @param scope Where the disk legend is drawn.
 * @param metrics The reading the screen was answered with.
 * @returns Resolves once every assertion held.
 */
const expectThreeFigures = async (scope: Locator, metrics: HostMetrics): Promise<void> => {
  const legend = scope.getByRole('list').filter({ hasText: 'Reserved for the system' })
  const row = (label: string): Locator => {
    return legend.getByRole('listitem').filter({ hasText: label })
  }
  await expect(row('In use')).toContainText(formatBytes(metrics.diskUsedBytes))
  await expect(row('Reserved for the system')).toContainText(formatBytes(metrics.diskReservedBytes))
  await expect(row('Available')).toContainText(formatBytes(metrics.diskAvailableBytes))
  await expect(legend.getByRole('listitem')).toHaveCount(3)

  const summary =
    `Disk: ${formatBytes(metrics.diskUsedBytes)} in use, ` +
    `${formatBytes(metrics.diskReservedBytes)} reserved for the system, ` +
    `${formatBytes(metrics.diskAvailableBytes)} available, of ${formatBytes(metrics.diskTotalBytes)}`
  await expect(scope.getByRole('img', { name: summary, exact: true })).toBeVisible()
}

test('an ext4 root shows used, reserved and available on the dashboard and on monitoring, summing to the size', async ({
  page,
}) => {
  // The payload's own arithmetic, which is the contract the screens rely on.
  expect(EXT4.diskUsedBytes + EXT4.diskReservedBytes + EXT4.diskAvailableBytes).toBe(EXT4.diskTotalBytes)

  await openLanding(page, EXT4)
  const main = page.getByRole('main')

  // The issue's own numbers, as df prints them: 7.7 GiB used of 97.9, and about 4.9 held for root.
  await expect(main.getByText('/ 97.9 GiB')).toBeVisible()
  await expectThreeFigures(main, EXT4)
  expect(formatBytes(EXT4.diskUsedBytes)).toBe('7.7 GiB')
  expect(formatBytes(EXT4.diskReservedBytes)).toBe('4.9 GiB')
  expect(formatBytes(EXT4.diskAvailableBytes)).toBe('85.3 GiB')

  // The size the tile prints is the size of the three parts together, not a figure of its own.
  const sum = EXT4.diskUsedBytes + EXT4.diskReservedBytes + EXT4.diskAvailableBytes
  await expect(main.getByText(`/ ${formatBytes(sum)}`)).toBeVisible()

  const card = await clickToMonitoring(page)
  await expect(card.getByRole('heading', { name: 'Disk', exact: true })).toBeVisible()
  await expect(card.getByText(`${formatBytes(EXT4.diskUsedBytes)} / ${formatBytes(sum)}`)).toBeVisible()
  await expectThreeFigures(card, EXT4)
})

test('an xfs root keeps the reserved row and shows it as zero on both screens', async ({ page }) => {
  expect(XFS.diskUsedBytes + XFS.diskReservedBytes + XFS.diskAvailableBytes).toBe(XFS.diskTotalBytes)

  await openLanding(page, XFS)
  const main = page.getByRole('main')
  await expectThreeFigures(main, XFS)
  await expect(
    main.getByRole('listitem').filter({ hasText: 'Reserved for the system' }),
  ).toContainText('0 B')

  const card = await clickToMonitoring(page)
  await expectThreeFigures(card, XFS)
  await expect(card.getByRole('listitem').filter({ hasText: 'Reserved for the system' })).toContainText('0 B')
})

test('a refused live reading is an alert in the disk card and leaves the charts drawn', async ({ page }) => {
  await openLanding(page, EXT4)
  await page.route(/\/api\/v1\/monitoring\/metrics$/, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Unexpected error', detail: 'The host reading could not be taken.' }),
    })
  })

  const card = await clickToMonitoring(page)
  await expect(card.getByText('The host reading could not be taken.')).toBeVisible()
  await expect(card.getByRole('list')).toHaveCount(0)
  await expect(page.getByTestId('monitoring-charts')).toBeVisible()
})
