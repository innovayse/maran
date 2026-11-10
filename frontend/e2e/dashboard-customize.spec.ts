import { expect, test } from './fixtures/guarded-test'
import { openLiveDashboard } from './fixtures/open-live-dashboard'
import { DEFAULT_LAYOUT } from './fixtures/stub-live-dashboard-stream'
import type { DashboardLayoutEntry } from '../src/types/dashboardLayout'
import type { Page } from '@playwright/test'

// Issue #199 (slice S1): a person arranges their own dashboard in the customise sheet: switch each
// widget and shortcut on or off, move it up or down. The layout lives on the panel per person
// (`/api/v1/me/dashboard-layout`), so a change must be SENT, and what the dashboard then draws is
// what the panel answers with. Reordering is buttons, never drag alone.
//
// BLIND SPOT: the layout endpoint is a stub that stores what it is sent, so this proves what the
// SPA sends and draws, not that the panel validates the keys, caps the list or isolates people
// from one another; those are the backend tests' job.

/**
 * The widgets in the order the dashboard draws them.
 * @param page The page under test.
 * @returns The widget keys, top to bottom.
 */
const drawnWidgets = async (page: Page): Promise<(string | null)[]> => {
  return page.getByTestId('dashboard-widgets').locator('[data-widget]').evaluateAll((cells) => {
    return cells.map((cell) => {
      return cell.getAttribute('data-widget')
    })
  })
}

/**
 * The keys of a list of layout entries, in order.
 * @param entries The entries.
 * @returns The keys.
 */
const keysOf = (entries: DashboardLayoutEntry[]): string[] => {
  return entries.map((entry) => {
    return entry.key
  })
}

/**
 * The keys of the entries that are not hidden, in order.
 * @param entries The entries.
 * @returns The keys.
 */
const shownKeys = (entries: DashboardLayoutEntry[]): string[] => {
  return keysOf(
    entries.filter((entry) => {
      return !entry.hidden
    }),
  )
}

/**
 * The entry with a key.
 * @param entries The entries.
 * @param key The key to find.
 * @returns The entry, or `undefined`.
 */
const entryOf = (entries: DashboardLayoutEntry[], key: string): DashboardLayoutEntry | undefined => {
  return entries.find((entry) => {
    return entry.key === key
  })
}

test.describe('customising the dashboard', () => {
  test('hiding a widget sends the layout and the dashboard stops drawing it', async ({ page }) => {
    const layout = await openLiveDashboard(page)
    expect(await drawnWidgets(page)).toContain('network')

    await page.getByTestId('customize-open').click()
    const sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })
    await sheet.getByRole('switch', { name: 'Network' }).click()
    await sheet.getByTestId('customize-save').click()

    await expect(sheet).toHaveCount(0)
    expect(layout.saved).toHaveLength(1)
    expect(entryOf(layout.saved[0].widgets, 'network')).toEqual({ key: 'network', hidden: true })
    // Everything else is unchanged and still in the panel's order.
    expect(shownKeys(layout.saved[0].widgets)).toEqual(
      shownKeys(DEFAULT_LAYOUT.widgets).filter((key) => {
        return key !== 'network'
      }),
    )
    expect(await drawnWidgets(page)).not.toContain('network')
  })

  test('moving a widget up with the keyboard changes the order that is sent and drawn', async ({ page }) => {
    const layout = await openLiveDashboard(page)
    await page.getByTestId('customize-open').click()
    const sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })

    const moveUp = sheet.getByRole('button', { name: 'Move Disk up' })
    await moveUp.focus()
    await page.keyboard.press('Enter')
    await sheet.getByTestId('customize-save').click()

    await expect(sheet).toHaveCount(0)
    const sent = keysOf(layout.saved[0].widgets)
    expect(sent.indexOf('disk')).toBeLessThan(sent.indexOf('memory'))
    const drawn = await drawnWidgets(page)
    expect(drawn.indexOf('disk')).toBeLessThan(drawn.indexOf('memory'))
  })

  test('the first row cannot move up and the last cannot move down', async ({ page }) => {
    await openLiveDashboard(page)
    await page.getByTestId('customize-open').click()
    const sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })

    await expect(sheet.getByRole('button', { name: 'Move Needs attention up' })).toBeDisabled()
    await expect(sheet.getByRole('button', { name: 'Move Latest activity down' })).toBeDisabled()
    // The inverse control: a row in the middle can move both ways.
    await expect(sheet.getByRole('button', { name: 'Move Memory up' })).toBeEnabled()
    await expect(sheet.getByRole('button', { name: 'Move Memory down' })).toBeEnabled()
  })

  test('closing the sheet without saving discards the edits and sends nothing', async ({ page }) => {
    const layout = await openLiveDashboard(page)
    await page.getByTestId('customize-open').click()
    let sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })
    await sheet.getByRole('switch', { name: 'Network' }).click()
    await expect(sheet.getByRole('switch', { name: 'Network' })).not.toBeChecked()

    await page.keyboard.press('Escape')
    await expect(sheet).toHaveCount(0)
    // Focus returns to the control that opened it.
    await expect(page.getByTestId('customize-open')).toBeFocused()

    await page.getByTestId('customize-open').click()
    sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })
    await expect(sheet.getByRole('switch', { name: 'Network' })).toBeChecked()
    expect(layout.saved).toHaveLength(0)
  })

  test('a key this bundle cannot draw is kept in place and sent back, not dropped', async ({ page }) => {
    const layout = await openLiveDashboard(page, {
      layout: {
        ...DEFAULT_LAYOUT,
        widgets: [...DEFAULT_LAYOUT.widgets, { key: 'mailQueue', hidden: false }],
      },
    })
    // Skipped on the dashboard, and not offered in the sheet.
    expect(await drawnWidgets(page)).not.toContain('mailQueue')

    await page.getByTestId('customize-open').click()
    const sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })
    await sheet.getByRole('switch', { name: 'Network' }).click()
    await sheet.getByTestId('customize-save').click()

    await expect(sheet).toHaveCount(0)
    expect(layout.saved[0].widgets.at(-1)).toEqual({ key: 'mailQueue', hidden: false })
  })

  test('a layout the panel refuses keeps the sheet open with the panel\'s reason', async ({ page, guard }) => {
    await openLiveDashboard(page)
    // The refusal of the save is the subject of this test.
    guard.allow(400, /\/api\/v1\/me\/dashboard-layout$/)
    await page.route(/\/api\/v1\/me\/dashboard-layout(\?.*)?$/, async (route) => {
      if (route.request().method() === 'PUT') {
        await route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Invalid', status: 400, detail: 'A layout may hold at most 32 entries.' }),
        })
        return
      }
      await route.fallback()
    })

    await page.getByTestId('customize-open').click()
    const sheet = page.getByRole('dialog', { name: 'Customize the dashboard' })
    await sheet.getByRole('switch', { name: 'Network' }).click()
    await sheet.getByTestId('customize-save').click()

    await expect(sheet.getByText('A layout may hold at most 32 entries.')).toBeVisible()
    // Nothing the person arranged was lost.
    await expect(sheet.getByRole('switch', { name: 'Network' })).not.toBeChecked()
  })

  test('a layout that cannot be read still shows every widget, and offers no customise control', async ({ page, guard }) => {
    await openLiveDashboard(page)
    // The unreadable layout is the subject of this test.
    guard.allow(503, /\/api\/v1\/me\/dashboard-layout$/)
    await page.route(/\/api\/v1\/me\/dashboard-layout(\?.*)?$/, async (route) => {
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Unavailable', status: 503 }) })
    })
    await page.reload()

    await expect(page.getByTestId('dashboard-widgets')).toBeVisible()
    expect(await drawnWidgets(page)).toEqual(keysOf(DEFAULT_LAYOUT.widgets))
    await expect(page.getByTestId('customize-open')).toHaveCount(0)
  })
})
