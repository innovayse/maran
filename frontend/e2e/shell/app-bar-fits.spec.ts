import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubTaskStream, stubTasks, taskFrame } from '../fixtures/stub-tasks-routes'
import type { PanelModule } from '../../src/types/module'
import type { PanelTask } from '../../src/types/panelTask'

/**
 * Protects the app bar's fit on a phone (issue #82): with the running-tasks badge showing, the
 * bar's controls must all lie inside the viewport at 390px and at 320px, in every language.
 *
 * Why a dedicated spec: the shell clips overflow, so a bar that is wider than the screen does not
 * scroll the document. It simply cuts its right-hand controls off (the language picker half drawn,
 * the avatar gone), and the page-level "no horizontal scroll" specs read that as a pass. The only
 * honest probe is the geometry of the bar's own controls.
 *
 * It also protects the bar under a larger TEXT size (WCAG 1.4.4): 200% at a 1280px window and
 * 150% at 640px. Those leave the window as wide as it was and the bar several times too narrow,
 * which a media query cannot see; the controls then overflowed (the language picker and avatar
 * off the canvas, the `⌘K` hint on top of the tasks badge, the breadcrumb cut to "Pa…").
 */

const LICENSED: PanelModule[] = [
  { name: 'tasks', displayName: 'Background tasks', tier: 'included', isEnabled: true },
]

/** A task still running, so the tasks badge is drawn: the widest the app bar ever is. */
const RUNNING: PanelTask = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'CertificateOrder',
  subject: 'alice.example.com',
  correlationId: null,
  status: 'running',
  percent: 40,
  log: '',
  errorCode: null,
  startedAt: '2026-09-03T09:00:00Z',
  finishedAt: null,
  revision: 5,
}

/** The phone widths the criterion names: the common one and the narrowest supported. */
const WIDTHS = [390, 320] as const

/** The interface languages the panel ships. */
const LOCALES = ['en', 'ru', 'hy'] as const

/**
 * Brings the dashboard up with a running task, so the badge is on screen.
 * @param page The page to prepare.
 * @returns Resolves once the badge is visible.
 */
const openWithBadge = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubTasks(page, [RUNNING])
  await stubTaskStream(page, taskFrame(RUNNING))
  await page.goto('/')
  // By destination, not by name: the badge's accessible name is translated and this spec runs in
  // every language.
  await expect(page.getByRole('banner').locator('a[href="/tasks"]')).toBeVisible()
}

for (const width of WIDTHS) {
  for (const locale of LOCALES) {
    test(`the app bar fits at ${width.toString()}px in ${locale} with the tasks badge showing`, async ({ page }) => {
      await page.setViewportSize({ width, height: 844 })
      await setPersistedLocale(page, locale)
      await openWithBadge(page)

      const banner = page.getByRole('banner')
      const overflow = await banner.evaluate((header: HTMLElement): { clipped: boolean; outside: string[] } => {
        const bar = header.getBoundingClientRect()
        const outside: string[] = []
        for (const control of header.querySelectorAll<HTMLElement>('a, button')) {
          const box = control.getBoundingClientRect()
          // Hidden controls have no box; only what is drawn can poke out.
          if (box.width === 0 && box.height === 0) {
            continue
          }
          if (box.left < bar.left - 0.5 || box.right > bar.right + 0.5) {
            outside.push(`${control.getAttribute('aria-label') ?? control.textContent?.trim() ?? control.tagName}`)
          }
        }
        return { clipped: header.scrollWidth > header.clientWidth, outside }
      })

      expect(overflow.outside).toEqual([])
      expect(overflow.clipped).toBe(false)
    })
  }
}

/** Text-only enlargements: the window's width is unchanged, only the root font size grows. */
const TEXT_SIZES = [
  { width: 1280, percent: 200 },
  { width: 640, percent: 150 },
  { width: 1024, percent: 200 },
] as const

for (const { width, percent } of TEXT_SIZES) {
  for (const locale of LOCALES) {
    test(`the app bar holds together at ${width.toString()}px with ${percent.toString()}% text in ${locale}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 })
      await setPersistedLocale(page, locale)
      await openWithBadge(page)
      await page.addStyleTag({ content: `html { font-size: ${percent.toString()}% !important; }` })

      const banner = page.getByRole('banner')
      const report = await banner.evaluate((header: HTMLElement): { outside: string[]; overlapping: string[]; clipped: boolean; cut: string[] } => {
        const bar = header.getBoundingClientRect()
        const drawn = [...header.querySelectorAll<HTMLElement>('a, button, kbd, nav li')].filter((element) => {
          const box = element.getBoundingClientRect()
          return getComputedStyle(element).visibility !== 'hidden' && box.width > 0 && box.height > 0
        })
        const name = (element: HTMLElement): string => {
          return element.getAttribute('aria-label') ?? element.textContent?.trim() ?? element.tagName
        }
        const outside = drawn
          .filter((element) => {
            const box = element.getBoundingClientRect()
            return box.left < bar.left - 0.5 || box.right > bar.right + 0.5
          })
          .map(name)
        const overlapping: string[] = []
        for (const [index, first] of drawn.entries()) {
          for (const second of drawn.slice(index + 1)) {
            // A control that contains the other (the search field holds its hint) is not a collision.
            if (first.contains(second) || second.contains(first)) {
              continue
            }
            const a = first.getBoundingClientRect()
            const b = second.getBoundingClientRect()
            const across = Math.min(a.right, b.right) - Math.max(a.left, b.left)
            const down = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top)
            if (across > 1 && down > 1) {
              overlapping.push(`${name(first)} / ${name(second)}`)
            }
          }
        }
        // A breadcrumb entry that is drawn must be drawn whole ("Pa…" is the failure).
        const cut = drawn
          .filter((element) => {
            return element.tagName === 'LI' && element.scrollWidth > element.clientWidth + 1
          })
          .map(name)
        return { outside, overlapping, clipped: header.scrollWidth > header.clientWidth, cut }
      })

      expect(report.outside).toEqual([])
      expect(report.overlapping).toEqual([])
      expect(report.cut).toEqual([])
      expect(report.clipped).toBe(false)
      // Whatever else gave way, the person can still open the palette and the language menu.
      await expect(banner.locator('button[aria-keyshortcuts]:visible')).toHaveCount(1)
      await expect(banner.locator('button[aria-haspopup="menu"]:visible').first()).toBeVisible()
    })
  }
}

test('below sm the theme switch is reached from the navigation drawer, with its name unchanged', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await openWithBadge(page)

  // Not in the app bar at this width...
  await expect(page.getByRole('banner').getByRole('button', { name: 'Switch between the dark and light theme' })).toHaveCount(0)

  // ...but one tap away, in the drawer both of the shell's openers lead to.
  await page.getByRole('button', { name: 'Open the navigation' }).click()
  const drawer = page.getByRole('dialog', { name: 'Navigation' })
  await expect(drawer.getByRole('button', { name: 'Switch between the dark and light theme' })).toBeVisible()
})
