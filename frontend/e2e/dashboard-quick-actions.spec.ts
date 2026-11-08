import { expect, test } from '@playwright/test'
import { FULL_CATALOGUE } from './fixtures/open-full-shell'
import { openLiveDashboard } from './fixtures/open-live-dashboard'
import { stubFirewall } from './fixtures/stub-firewall-routes'
import { DEFAULT_LAYOUT } from './fixtures/stub-live-dashboard-stream'

// Issue #199 (slice S1): the quick-actions bar. Every shortcut is a link to the screen that owns
// the work; creating something is `?action=create` on that screen, which brings its own form into
// view and focuses the first field, then removes the parameter. The bar is the person's layout
// filtered by the licence.
//
// BLIND SPOT: only the firewall bans screen is rendered end to end here (the receiving side is one
// composable shared by four screens: databases, backups, cron, firewall); the other three are
// asserted by the address their shortcut carries. The licence filter is the SPA's advice: the
// panel refuses the request regardless.

/** Each shortcut and the address it leads to. */
const DESTINATIONS: readonly (readonly [string, string])[] = [
  ['createAccount', '/accounts/new'],
  ['addSite', '/sites/new'],
  ['createDatabase', '/databases?action=create'],
  ['runBackup', '/backups?action=create'],
  ['banAddress', '/firewall/bans?action=create'],
  ['addCronJob', '/cron?action=create'],
]

test.describe('the quick-actions bar', () => {
  test('every shortcut is a link to the screen that owns the work', async ({ page }) => {
    await openLiveDashboard(page)

    for (const [key, address] of DESTINATIONS) {
      await expect(page.getByTestId(`quick-action-${key}`)).toHaveAttribute('href', address)
    }
    await expect(page.getByTestId('quick-actions-bar').getByRole('link')).toHaveCount(DESTINATIONS.length)
  })

  test('a shortcut can be reached and followed with the keyboard alone', async ({ page }) => {
    await openLiveDashboard(page)

    await page.getByTestId('quick-action-createAccount').focus()
    await page.keyboard.press('Tab')
    await expect(page.getByTestId('quick-action-addSite')).toBeFocused()

    await page.getByTestId('quick-action-createAccount').focus()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/accounts\/new$/)
  })

  test('the create shortcut opens the screen on its own form, focused, and clears the parameter', async ({ page }) => {
    await openLiveDashboard(page)
    await stubFirewall(page, { rules: [], bans: [], whitelist: [] })

    await page.getByTestId('quick-action-banAddress').click()

    await expect(page.getByLabel('Address')).toBeFocused()
    // Gone from the address, so a reload or Back does not do it again.
    await expect(page).toHaveURL(/\/firewall\/bans$/)
  })

  test('the same screen opened without the shortcut does not take focus', async ({ page }) => {
    // The inverse control of the test above: focus on the field is the shortcut's doing, not the screen's.
    await openLiveDashboard(page)
    await stubFirewall(page, { rules: [], bans: [], whitelist: [] })

    await page.goto('/firewall/bans')

    await expect(page.getByLabel('Address')).toBeVisible()
    await expect(page.getByLabel('Address')).not.toBeFocused()
  })

  test('a shortcut the person hid is not offered', async ({ page }) => {
    await openLiveDashboard(page, {
      layout: {
        ...DEFAULT_LAYOUT,
        actions: DEFAULT_LAYOUT.actions.map((entry) => {
          return entry.key === 'banAddress' ? { ...entry, hidden: true } : entry
        }),
      },
    })

    await expect(page.getByTestId('quick-action-addSite')).toBeVisible()
    await expect(page.getByTestId('quick-action-banAddress')).toHaveCount(0)
  })

  test('a shortcut whose module is not licensed is not offered', async ({ page }) => {
    const unlicensed = new Set(['firewall', 'cron'])
    await openLiveDashboard(page, {
      modules: FULL_CATALOGUE.filter((module) => {
        return !unlicensed.has(module.name)
      }),
    })

    await expect(page.getByTestId('quick-action-addSite')).toBeVisible()
    await expect(page.getByTestId('quick-action-banAddress')).toHaveCount(0)
    await expect(page.getByTestId('quick-action-addCronJob')).toHaveCount(0)
  })
})
