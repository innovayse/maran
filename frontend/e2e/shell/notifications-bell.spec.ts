import { expect, test, type Locator, type Page } from '@playwright/test'
import { openFullShell } from '../fixtures/open-full-shell'
import { feedEntry, feedState, stubNotificationFeed, type NotificationFeedState } from '../fixtures/stub-notification-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubResellerSession } from '../fixtures/stub-reseller-session'

// Issue #164: the app bar's notification bell, for administrators. It shows how many entries the
// signed-in administrator has not dismissed, opens the feed in a popover (a bottom sheet on a
// phone), lets the person dismiss one or all of them, and follows an entry's link to the screen it
// is about. Dismissal is per administrator and lives on the panel; these specs drive the SPA
// against a stub that behaves the way the panel does. Navigation is by clicks only.

const FAILED_BACKUP = feedEntry({
  id: '00000000-0000-0000-0000-000000000001',
  source: 'backup',
  severity: 'critical',
  title: 'The scheduled backup of alice failed',
  detail: 'The destination refused the upload.',
  deepLink: 'backups',
  occurredAt: '2026-11-06T12:00:00+00:00',
})

const FAILED_TASK = feedEntry({
  id: '00000000-0000-0000-0000-000000000002',
  source: 'task',
  severity: 'critical',
  title: 'Issuing a certificate for alice.example.com failed',
  deepLink: 'tasks',
  occurredAt: '2026-11-06T11:00:00+00:00',
})

const DISK_ALERT = feedEntry({
  id: '00000000-0000-0000-0000-000000000003',
  source: 'alert',
  severity: 'warning',
  title: 'Disk usage is above 90%',
  detail: '93% of 98 GB is in use.',
  occurredAt: '2026-11-06T10:00:00+00:00',
})

/**
 * Opens the administrator's shell with the feed answering what a spec holds. The feed is installed
 * after the shell's catch-all so it wins, and the page is reloaded because the bell reads its
 * figure when it mounts.
 * @param page The Playwright page under test.
 * @param state What the stubbed panel holds for the administrator.
 * @returns Resolves once the reloaded shell is drawn.
 */
const openWithFeed = async (page: Page, state: NotificationFeedState): Promise<void> => {
  await openFullShell(page)
  await stubNotificationFeed(page, state)
  await page.reload()
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
}

/**
 * The bell's trigger, found by role and by the start of its name (the name carries the figure).
 * @param page The Playwright page under test.
 * @param name The word the name starts with, in the interface language.
 * @returns Its locator.
 */
const bell = (page: Page, name = 'Notifications'): Locator => {
  return page.getByRole('banner').getByRole('button', { name: new RegExp(`^${name}`) })
}

test('an empty feed draws a bell with no badge and an empty state when opened', async ({ page }) => {
  await openWithFeed(page, feedState([]))

  await expect(bell(page)).toBeVisible()
  await expect(bell(page)).toHaveAccessibleName('Notifications')
  await expect(page.getByTestId('notifications-badge')).toHaveCount(0)

  await bell(page).click()
  const dialog = page.getByRole('dialog', { name: 'Notifications' })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText('No notifications')).toBeVisible()
  // Nothing to dismiss, so the action is not offered.
  await expect(dialog.getByRole('button', { name: 'Dismiss all' })).toHaveCount(0)
})

test('the badge shows how many entries are not dismissed and the name says so', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT]))

  await expect(bell(page)).toHaveAccessibleName('Notifications, 3 not dismissed')
  await expect(page.getByTestId('notifications-badge')).toHaveText('3')
})

test('past the cap the badge reads 99+ and the name says more than 99', async ({ page }) => {
  const many = Array.from({ length: 120 }, (_, index) => {
    return feedEntry({ id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, '0')}`, title: `Task ${index + 1} failed` })
  })
  await openWithFeed(page, feedState(many))

  await expect(page.getByTestId('notifications-badge')).toHaveText('99+')
  await expect(bell(page)).toHaveAccessibleName('Notifications, more than 99 not dismissed')

  // The list holds one page; the footer says how many more exist rather than implying completeness.
  await bell(page).click()
  await expect(page.getByTestId('notification-row')).toHaveCount(25)
  await expect(page.getByText('95 more are not shown')).toBeVisible()
})

test('opening the bell lists the entries newest first with their detail', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT]))

  await bell(page).click()
  const rows = page.getByTestId('notification-row')
  await expect(rows).toHaveCount(3)
  await expect(rows.nth(0)).toContainText('The scheduled backup of alice failed')
  await expect(rows.nth(0)).toContainText('The destination refused the upload.')
  await expect(rows.nth(2)).toContainText('Disk usage is above 90%')
})

test('dismissing one entry removes only that one and lowers the badge', async ({ page }) => {
  const state = feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT])
  await openWithFeed(page, state)
  await bell(page).click()

  await page.getByRole('button', { name: `Dismiss: ${FAILED_TASK.title}` }).click()

  await expect(page.getByTestId('notification-row')).toHaveCount(2)
  await expect(page.getByText(FAILED_TASK.title)).toHaveCount(0)
  // The others are still there: the removal was of this entry alone.
  await expect(page.getByText(FAILED_BACKUP.title)).toBeVisible()
  await expect(page.getByText(DISK_ALERT.title)).toBeVisible()
  await expect(page.getByTestId('notifications-badge')).toHaveText('2')
  expect(state.dismissed).toEqual([FAILED_TASK.id])
})

test('dismissing all empties the feed and removes the badge', async ({ page }) => {
  const state = feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT])
  await openWithFeed(page, state)
  await bell(page).click()

  await page.getByRole('dialog').getByRole('button', { name: 'Dismiss all' }).click()

  await expect(page.getByText('No notifications')).toBeVisible()
  await expect(page.getByTestId('notifications-badge')).toHaveCount(0)
  expect(state.dismissAllCalls).toBe(1)
})

test('a refused dismissal says why in the panel\'s words and keeps the entry', async ({ page }) => {
  const state = feedState([FAILED_TASK])
  state.refuseDismissWith = 'The notification could not be dismissed right now.'
  await openWithFeed(page, state)
  await bell(page).click()

  await page.getByRole('button', { name: `Dismiss: ${FAILED_TASK.title}` }).click()

  await expect(page.getByText('The notification could not be dismissed right now.')).toBeVisible()
  await expect(page.getByText(FAILED_TASK.title)).toBeVisible()
})

test('a feed that cannot be read says so and reads again on retry', async ({ page }) => {
  const state = feedState([FAILED_TASK])
  state.failListWith = 503
  await openWithFeed(page, state)
  await bell(page).click()

  await expect(page.getByText('The feed could not be read.')).toBeVisible()
  // The badge figure is a separate read and still answered: a failed list is not a failed bell.
  await expect(page.getByTestId('notifications-badge')).toHaveText('1')

  state.failListWith = null
  await page.getByRole('button', { name: 'Try again' }).click()
  await expect(page.getByText(FAILED_TASK.title)).toBeVisible()
})

test('an entry\'s link opens the screen it is about and closes the feed', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_TASK]))
  await bell(page).click()

  await page.getByRole('dialog').getByRole('link', { name: 'Open' }).click()

  await expect(page).toHaveURL('/tasks')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('an entry naming a page this panel does not know draws no link', async ({ page }) => {
  const known = feedEntry({ id: '00000000-0000-0000-0000-0000000000a1', title: 'Known page', deepLink: 'tasks' })
  const unknown = feedEntry({ id: '00000000-0000-0000-0000-0000000000a2', title: 'Unknown page', deepLink: 'nowhere-at-all' })
  await openWithFeed(page, feedState([known, unknown]))
  await bell(page).click()

  // Positive control: the known entry has its link, so the unknown one lacking it is the SPA's choice.
  await expect(page.getByTestId('notification-row').filter({ hasText: 'Known page' }).getByRole('link', { name: 'Open' })).toBeVisible()
  await expect(page.getByTestId('notification-row').filter({ hasText: 'Unknown page' }).getByRole('link')).toHaveCount(0)
})

test('the keyboard opens the feed, moves focus in, and Escape closes it and returns focus', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_TASK]))

  await bell(page).focus()
  await page.keyboard.press('Enter')
  const dialog = page.getByRole('dialog', { name: 'Notifications' })
  await expect(dialog).toBeVisible()
  await expect(bell(page)).toHaveAttribute('aria-expanded', 'true')
  await expect(dialog.getByRole('button', { name: 'Close notifications' })).toBeFocused()

  // Tab stays inside the panel: after the last control it wraps to the first.
  for (let press = 0; press < 6; press += 1) {
    await page.keyboard.press('Tab')
    expect(await dialog.evaluate((panel) => {return panel.contains(document.activeElement)})).toBe(true)
  }

  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(bell(page)).toBeFocused()
  await expect(bell(page)).toHaveAttribute('aria-expanded', 'false')
})

test('a press outside the open feed closes it on the desktop', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_TASK]))
  await bell(page).click()
  await expect(page.getByRole('dialog')).toBeVisible()

  await page.getByRole('main').click({ position: { x: 5, y: 5 } })

  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('on a phone the feed is a modal bottom sheet and the page does not scroll sideways', async ({ page }) => {
  await openWithFeed(page, feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT]))
  await page.setViewportSize({ width: 390, height: 844 })

  await bell(page).click()
  const dialog = page.getByRole('dialog', { name: 'Notifications' })
  await expect(dialog).toBeVisible()
  await expect(dialog).toHaveAttribute('aria-modal', 'true')

  const box = await dialog.boundingBox()
  expect(box).not.toBeNull()
  // A sheet is anchored to the bottom edge and spans the width.
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeGreaterThan(800)
  expect(box?.width ?? 0).toBeGreaterThan(380)
  const extent = await page.evaluate(() => {return { scroll: document.documentElement.scrollWidth, inner: window.innerWidth }})
  expect(extent.scroll).toBeLessThanOrEqual(extent.inner)

  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
})

test('a reseller\'s shell has no bell and never asks for the feed', async ({ page }) => {
  const urls: string[] = []
  page.on('request', (request) => {
    urls.push(request.url())
  })
  await stubResellerSession(page)

  await page.goto('/')
  await expect(page.getByRole('banner')).toBeVisible()

  await expect(bell(page)).toHaveCount(0)
  expect(urls.filter((url) => {return url.includes('/api/v1/notifications/')})).toEqual([])
})

test('the administrator\'s shell does ask for the feed, so the observer above is not blind', async ({ page }) => {
  const urls: string[] = []
  page.on('request', (request) => {
    urls.push(request.url())
  })
  await openWithFeed(page, feedState([]))

  await expect.poll(() => {
    return urls.filter((url) => {return url.includes('/api/v1/notifications/feed/unread-count')}).length
  }).toBeGreaterThan(0)
})

const LOCALES = [
  { code: 'ru', name: 'Уведомления', unread: 'Уведомления, не скрыто: 3', empty: 'Уведомлений нет', dismissAll: 'Скрыть все' },
  { code: 'hy', name: 'Ծանուցումներ', unread: 'Ծանուցումներ, չփակված՝ 3', empty: 'Ծանուցումներ չկան', dismissAll: 'Փակել բոլորը' },
] as const

for (const locale of LOCALES) {
  test(`the bell speaks ${locale.code}: its name, the count, the empty state and the actions`, async ({ page }) => {
    await setPersistedLocale(page, locale.code)
    await openWithFeed(page, feedState([FAILED_BACKUP, FAILED_TASK, DISK_ALERT]))

    await expect(bell(page, locale.name)).toHaveAccessibleName(locale.unread)
    await bell(page, locale.name).click()
    const dialog = page.getByRole('dialog', { name: locale.name })
    await expect(dialog.getByRole('button', { name: locale.dismissAll })).toBeVisible()

    await dialog.getByRole('button', { name: locale.dismissAll }).click()
    await expect(dialog.getByText(locale.empty)).toBeVisible()
  })
}
