import { expect, test, type Page } from '@playwright/test'
import { holdRoute } from '../fixtures/hold-route'
import { EXAMPLE_ZONE, SHOP_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'

// Every server-paged list store assigns its rows through the one latest-read-wins guard
// (composables/useLatestRead.ts), so a slow older read cannot overwrite the rows of a newer one
// (issue #454). The zones list stands for all of them: they share the guard, not a copy of it.

/** The zone list read with no search term, which is the page's first read. */
const UNSEARCHED_LIST = /\/api\/v1\/dns-zones(\?(?!.*search=)[^/]*)?$/

/**
 * Lets the page paint and run its pending promise callbacks, so a row that was going to appear has.
 * @param page The page to wait on.
 * @returns Resolves after two animation frames.
 */
const settle = async (page: Page): Promise<void> => {
  await page.evaluate(() => {
    return new Promise<void>((resolve) => {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          resolve()
        })
      })
    })
  })
}

test('a slow older list read does not replace the rows of a newer search', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })
  const heldFirstRead = await holdRoute(page, UNSEARCHED_LIST)

  await page.goto('/dns/zones')
  await heldFirstRead.requested
  await page.getByRole('searchbox', { name: 'Search zones' }).fill('shop')
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()

  // The older read answers last, with BOTH zones; the vacuity guard is that it really answered.
  const olderAnswer = page.waitForResponse((response) => {
    return UNSEARCHED_LIST.test(response.url())
  })
  heldFirstRead.release()
  await (await olderAnswer).finished()
  await settle(page)

  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)
})

test('the only read in flight is shown when it answers, the control for the stale-read test above', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })
  const heldRead = await holdRoute(page, UNSEARCHED_LIST)

  await page.goto('/dns/zones')
  await heldRead.requested
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)

  heldRead.release()

  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
})
