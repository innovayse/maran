import { expect, test, type Page } from '@playwright/test'

// `UiPagination` through `e2e/fixtures/list-kit-harness.html` (see that file): the controls and
// their ARIA on a desktop, the compact row on a phone, and no sideways scroll in any language.

const PHONE = { width: 390, height: 844 }
const DESKTOP = { width: 1440, height: 900 }

/**
 * Opens the harness with a given total and starting page.
 * @param page The Playwright page.
 * @param query The harness's query string.
 * @returns Resolves once the pagination is rendered.
 */
const openHarness = async (page: Page, query: string): Promise<void> => {
  await page.goto(`/e2e/fixtures/list-kit-harness.html?${query}`)
  await expect(page.getByTestId('pagination')).toBeVisible()
}

test('the pagination is a named nav landmark marking the current page', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openHarness(page, 'total=312')

  const nav = page.getByRole('navigation', { name: 'Pagination' })
  await expect(nav).toBeVisible()
  await expect(nav.getByRole('button', { name: 'Page 1', exact: true })).toHaveAttribute('aria-current', 'page')
  await expect(nav.getByRole('button', { name: 'Page 2', exact: true })).not.toHaveAttribute('aria-current')
  await expect(page.getByTestId('pagination-range')).toHaveText('Showing 1–25 of 312')
  // 312 rows at 25 a page: pages 1, 2, a gap, then the last page, 13.
  await expect(nav.getByRole('button', { name: 'Page 13', exact: true })).toBeVisible()
  await expect(nav.getByRole('button', { name: 'Page 4', exact: true })).toHaveCount(0)
})

test('first and previous are disabled on page 1, next and last on the last page', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openHarness(page, 'total=312')
  const nav = page.getByRole('navigation', { name: 'Pagination' })

  await expect(nav.getByRole('button', { name: 'First page' })).toBeDisabled()
  await expect(nav.getByRole('button', { name: 'Previous page' })).toBeDisabled()

  await nav.getByRole('button', { name: 'Last page' }).click()
  await expect(nav.getByRole('button', { name: 'Page 13', exact: true })).toHaveAttribute('aria-current', 'page')
  await expect(nav.getByRole('button', { name: 'Next page' })).toBeDisabled()
  await expect(nav.getByRole('button', { name: 'Last page' })).toBeDisabled()
  await expect(page.getByTestId('pagination-range')).toHaveText('Showing 301–312 of 312')

  await nav.getByRole('button', { name: 'Previous page' }).click()
  await expect(nav.getByRole('button', { name: 'Page 12', exact: true })).toHaveAttribute('aria-current', 'page')
})

test('choosing a page size goes back to page 1', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openHarness(page, 'total=312&page=5')
  const nav = page.getByRole('navigation', { name: 'Pagination' })

  await nav.getByRole('combobox', { name: 'Rows per page' }).click()
  await page.getByRole('option', { name: '100' }).click()

  await expect(nav.getByRole('button', { name: 'Page 1', exact: true })).toHaveAttribute('aria-current', 'page')
  await expect(page.getByTestId('pagination-range')).toHaveText('Showing 1–100 of 312')
})

test('below sm the pagination is previous, the position and next', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openHarness(page, 'total=312&page=2')
  const nav = page.getByRole('navigation', { name: 'Pagination' })

  await expect(page.getByTestId('pagination-compact')).toContainText('Page 2 of 13')
  await expect(page.getByTestId('pagination-pages')).toBeHidden()
  await expect(page.getByTestId('pagination-range')).toBeHidden()
  await expect(nav.getByRole('button')).toHaveCount(2)

  await nav.getByRole('button', { name: 'Next page' }).click()
  await expect(page.getByTestId('pagination-compact')).toContainText('Page 3 of 13')
})

for (const locale of ['en', 'ru', 'hy']) {
  for (const viewport of [PHONE, DESKTOP]) {
    test(`the pagination does not scroll sideways at ${viewport.width}px in ${locale}`, async ({ page }) => {
      await page.setViewportSize(viewport)
      await openHarness(page, `locale=${locale}&total=12345&page=200`)

      const overflow = await page.evaluate(() => {
        const nav = document.querySelector('[data-testid="pagination"]')
        return {
          document: document.documentElement.scrollWidth - window.innerWidth,
          nav: nav === null ? -1 : nav.scrollWidth - nav.clientWidth,
        }
      })
      expect(overflow.document).toBeLessThanOrEqual(0)
      expect(overflow.nav).toBeLessThanOrEqual(0)
    })
  }
}
