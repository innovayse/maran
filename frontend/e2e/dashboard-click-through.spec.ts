import { expect, test } from '@playwright/test'
import { FULL_CATALOGUE } from './fixtures/open-full-shell'
import { goLive, openLiveDashboard } from './fixtures/open-live-dashboard'
import { loaded } from './fixtures/stub-dashboard-route'
import { pushFrame, reading, totals } from './fixtures/stub-live-dashboard-stream'

// Issue #199 (slice S1): every widget leads somewhere. A number on a dashboard is only useful if
// the reader can reach the screen that holds the detail in one step, by mouse or by keyboard, so
// each destination is asserted as a LINK with its address, never by a click that merely happened
// to land. A destination the licence does not permit is not drawn as a link at all.
//
// BLIND SPOT: the destination screens are not rendered here (their reads are not stubbed); only
// the address the link carries, and, for two of them, that following it reaches that address.

test.describe('the dashboard click-through', () => {
  test('the resource widgets open the monitoring screen on their own metric', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))

    for (const metric of ['cpu', 'memory', 'disk', 'network']) {
      await expect(page.getByTestId(`metric-head-${metric}`)).toHaveAttribute('href', `/monitoring?metric=${metric}`)
    }

    await page.getByTestId('metric-head-memory').click()
    await expect(page).toHaveURL(/\/monitoring\?metric=memory$/)
  })

  test('picking a point in a chart offers a link to that instant, which a keyboard can follow', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))

    const card = page.getByTestId('metric-card-cpu')
    await expect(card.getByRole('link', { name: /^Open .* in monitoring$/ })).toHaveCount(0)

    await card.getByTestId('live-chart-plot').click({ position: { x: 5, y: 20 } })
    const link = card.getByRole('link', { name: /^Open .* in monitoring$/ })
    await expect(link).toBeVisible()
    await expect(link).toHaveAttribute('href', /^\/monitoring\?metric=cpu&at=\d+$/)
    // Attached rather than visible: the marker is a vertical stroke, whose box is zero wide, which
    // Playwright reads as hidden whatever is drawn.
    await expect(card.getByTestId('live-chart-selected')).toBeAttached()

    await link.focus()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/monitoring\?metric=cpu&at=\d+$/)
  })

  test('the totals are links to the screens that own them', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)

    await expect(page.getByRole('link', { name: /^3 Accounts/ })).toHaveAttribute('href', '/accounts')
    await expect(page.getByRole('link', { name: /^7 Sites/ })).toHaveAttribute('href', '/sites')
    await expect(page.getByRole('link', { name: /^4 Databases/ })).toHaveAttribute('href', '/databases')
  })

  test('an attention row leads to the screen that holds it', async ({ page }) => {
    await openLiveDashboard(page)
    await goLive(page)
    await pushFrame(
      page,
      'totals',
      totals({ attention: { ...totals().attention, failedBackups: loaded(5) } }),
    )

    await expect(page.getByRole('link', { name: /backups failed/ })).toHaveAttribute('href', '/backups')
  })

  test('a screen whose module is not licensed is not offered as a link', async ({ page }) => {
    const withoutMonitoring = FULL_CATALOGUE.filter((module) => {
      return module.name !== 'monitoring' && module.name !== 'databases'
    })
    await openLiveDashboard(page, { modules: withoutMonitoring })
    await goLive(page)
    await pushFrame(page, 'metrics', reading(6))

    // The head is still there, with its figure, but it is not a link.
    await expect(page.getByTestId('metric-head-cpu')).toContainText('37.4%')
    await expect(page.getByTestId('metric-head-cpu')).not.toHaveAttribute('href', /.*/)
    await expect(page.getByRole('link', { name: /^4 Databases/ })).toHaveCount(0)
    // The inverse control: a licensed destination on the same screen IS a link.
    await expect(page.getByRole('link', { name: /^7 Sites/ })).toHaveAttribute('href', '/sites')
  })
})
