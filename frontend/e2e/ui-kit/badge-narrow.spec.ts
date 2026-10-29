import { expect, test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabaseGrants } from '../fixtures/stub-database-grant-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { GrantRepairReport } from '../../src/types/grantRepair'
import type { PanelModule } from '../../src/types/module'

/**
 * Protects the badge's two-sided promise (issue #82, WCAG 1.4.10): in a table it never shreds (that
 * half is measured by `table-narrow.spec.ts`), and outside a table it never outgrows its container.
 *
 * The longest badge the panel renders — "Inspection — nothing changed" and its Armenian equivalent,
 * on the grant-repair census card — used to be `whitespace-nowrap` with `shrink-0`, so at 320 px it
 * pushed 4 to 8 px past its card and the shell's `main` overflowed. It must now wrap at a word
 * boundary inside the card. Measured on geometry, never on a class name.
 */

const LICENSED: PanelModule[] = [{ name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true }]

/** An inspection report: the census card then carries the long "inspected" badge. */
const REPORT: GrantRepairReport = {
  isReportOnly: true,
  examinedGrants: 3,
  alreadyCorrect: 3,
  repaired: [],
  wouldRepair: [],
  refused: [],
}

for (const locale of ['en', 'hy'] as const) {
  test(`the long inspection badge stays inside its card at 320 px (${locale})`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await setPersistedLocale(page, locale)
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, LICENSED)
    await stubDatabaseGrants(page, REPORT)
    await page.goto('/databases/grant-repair')

    // The census badge is the only `rounded-full` pill in the page's main region that has a dot.
    const badge = page.locator('main span.rounded-full', { has: page.locator('span.rounded-full[aria-hidden="true"]') }).first()
    await expect(badge).toBeVisible({ timeout: 20_000 })
    const card = badge.locator('xpath=ancestor::div[contains(@class,"rounded-panel")][1]')

    const badgeBox = await badge.boundingBox()
    const cardBox = await card.first().boundingBox()
    expect(badgeBox).not.toBeNull()
    expect(cardBox).not.toBeNull()
    if (badgeBox === null || cardBox === null) {
      return
    }
    expect(badgeBox.x + badgeBox.width).toBeLessThanOrEqual(cardBox.x + cardBox.width + 0.5)
    expect(badgeBox.x).toBeGreaterThanOrEqual(cardBox.x - 0.5)

    // Wrapped at word boundaries only: every line of the badge's text holds whole words.
    const brokenWords = await badge.evaluate((element) => {
      const words = (element.textContent ?? '').trim().split(/\s+/)
      const range = document.createRange()
      const text = Array.from(element.childNodes).find((node) => {return node.nodeType === Node.TEXT_NODE && (node.textContent ?? '').trim() !== ''})
      if (text === undefined) {
        return -1
      }
      let broken = 0
      for (const word of words) {
        const start = (text.textContent ?? '').indexOf(word)
        range.setStart(text, start)
        range.setEnd(text, start + word.length)
        if (range.getClientRects().length > 1 && new Set(Array.from(range.getClientRects()).map((r) => {return Math.round(r.top)})).size > 1) {
          broken += 1
        }
      }
      return broken
    })
    expect(brokenWords).toBe(0)

    // The page itself does not scroll sideways.
    const overflow = await page.evaluate(() => {
      return document.documentElement.scrollWidth - document.documentElement.clientWidth
    })
    expect(overflow).toBeLessThanOrEqual(0)
    const mainOverflow = await page.locator('main').evaluate((element) => {
      return element.scrollWidth - element.clientWidth
    })
    expect(mainOverflow).toBeLessThanOrEqual(0)
  })
}
