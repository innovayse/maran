import { expect, test, type Locator } from '@playwright/test'
import { mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'

// Issue #209, point 3: every navigation label names the screen it opens. "Notifications" opened
// "Outgoing mail" and "Users and access" opened "Signed-in devices" (no user management there),
// while the security policy and the PHP versions had no entry at all.

/** One entry: its label, where it leads, and the section it sits in (when it is a child). */
interface Destination {
  /** The entry's label, which is also the page's title. */
  label: string
  /** Where the entry leads. */
  path: string
  /** The section whose submenu holds it, or `null` for a top-level entry. */
  section: string | null
}

const DESTINATIONS: Destination[] = [
  { label: 'Signed-in devices', path: '/settings/sessions', section: null },
  { label: 'Outgoing mail', path: '/settings/smtp', section: null },
  { label: 'Security policy', path: '/settings/security-policy', section: null },
  { label: 'PHP versions', path: '/settings/php', section: 'Sites' },
]

test('no entry is named for a module rather than for its screen', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)
  await expect(nav.getByRole('link', { name: 'Users and access' })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Notifications' })).toHaveCount(0)
})

for (const { label, path, section } of DESTINATIONS) {
  test(`"${label}" opens the screen titled "${label}"`, async ({ page }) => {
    await openFullShell(page)
    if (section !== null) {
      await openSection(page, section)
    }
    await mainNavigation(page).getByRole('link', { name: label, exact: true }).click()
    await expect(page).toHaveURL(path)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(label)
  })
}

/**
 * Counts the lines a navigation row's label is laid out on, from the text's own line boxes.
 * @param link The row.
 * @returns The number of distinct lines.
 */
const labelLines = async (link: Locator): Promise<number> => {
  return link.evaluate((element) => {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
    const tops = new Set<number>()
    for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
      if ((node.textContent ?? '').trim() === '') {
        continue
      }
      const range = document.createRange()
      range.selectNodeContents(node)
      for (const rect of range.getClientRects()) {
        tops.add(Math.round(rect.top))
      }
    }
    return tops.size
  })
}

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the renamed entries fit one line of the 216px sidebar in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await openFullShell(page)
    // The landmark's name is translated too, so the sidebar is found by its section rows.
    const nav = page.locator('nav:has([data-nav-section])')
    // The section toggles are named in the interface language, so the Sites section (whose submenu
    // holds PHP versions) is opened by the toggle beside its row.
    await nav.locator('[data-nav-section="sites"]').locator('xpath=..').getByRole('button').click()

    const renamed = ['/settings/sessions', '/settings/smtp', '/settings/security-policy', '/settings/php', '/tasks', '/cron']
    for (const href of renamed) {
      const link = nav.locator(`a[href="${href}"]`)
      await expect(link).toBeVisible()
      expect(await labelLines(link), `${href} in ${locale}`).toBe(1)
    }
  })
}
