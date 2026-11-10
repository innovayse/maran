import { expect, test } from '../fixtures/guarded-test'
import { breadcrumbItems, mainNavigation, openFullShell } from '../fixtures/open-full-shell'
import type { Page } from '@playwright/test'

// Issue #499: the new-site form and a cron entry's run history were reachable but unplaced in the
// navigation, so their eyebrow and breadcrumb read "Panel". A screen the sidebar does not list names
// the destination it sits under (`meta.navigationParent`), and the trail follows. The new-account
// form, which the tree lists itself, is the accepting control: it always read its section.

/** The page eyebrow over the title. */
const EYEBROW = 'main [data-ui="eyebrow"]'

/** A cron entry's id; the run history is routed under it. */
const ENTRY_ID = '11111111-1111-4111-8111-111111111111'

/**
 * Reads the trail as one string, crumbs joined the way they are drawn.
 * @param page The Playwright page under test.
 * @returns The trail.
 */
const trail = async (page: Page): Promise<string> => {
  const crumbs = await breadcrumbItems(page).allInnerTexts()
  return crumbs
    .map((crumb) => {
      return crumb.trim()
    })
    .join(' / ')
}

test('the new-site form is placed under Hosting and Sites and domains, not under Panel', async ({ page }) => {
  await openFullShell(page, '/sites/new')

  await expect(page.locator(EYEBROW).first()).toHaveText('Hosting')
  await expect.poll(() => {return trail(page)}).toBe('Hosting / Sites and domains / New site')
  // The section that holds the screen is lit, as it is for a site's own page.
  await expect(mainNavigation(page).getByRole('button', { name: 'Show the Sites and domains pages' })).toHaveAttribute(
    'aria-expanded',
    'true',
  )
})

test('a cron entry\'s run history is placed under Scheduled tasks', async ({ page }) => {
  await openFullShell(page, `/cron/${ENTRY_ID}/runs`)
  // The breadcrumb hides itself when its whole trail does not fit the app bar; four crumbs want the room.
  await page.setViewportSize({ width: 1680, height: 900 })

  await expect(page.locator(EYEBROW).first()).toHaveText('Hosting')
  await expect.poll(() => {return trail(page)}).toBe('Hosting / Files and access / Scheduled tasks / Run history')
})

test('the new-account form, which the tree lists itself, reads its section as before', async ({ page }) => {
  await openFullShell(page, '/accounts/new')

  await expect(page.locator(EYEBROW).first()).toHaveText('Hosting')
  await expect.poll(() => {return trail(page)}).toBe('Hosting / Accounts / New account')
})
