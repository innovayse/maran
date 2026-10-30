import { expect, test, type Page } from '@playwright/test'

// `UiFilterSheet` through `e2e/fixtures/list-kit-harness.html`, a Playwright-only mount point
// that renders the list kit without the router, shell or authentication (see that file).
//
// The contract: below `lg` the filters are a bottom sheet, from `lg` up the kit's centred dialog;
// either way Escape closes it and focus returns to the trigger, nothing is applied before Apply,
// and no language pushes the page sideways on a phone.

const PHONE = { width: 390, height: 844 }
const DESKTOP = { width: 1440, height: 900 }

/**
 * Opens the harness and its filters.
 * @param page The Playwright page.
 * @param locale The interface language.
 * @returns Resolves once the dialog is visible.
 */
const openFilters = async (page: Page, locale = 'en'): Promise<void> => {
  await page.goto(`/e2e/fixtures/list-kit-harness.html?locale=${locale}`)
  await page.getByTestId('filter-trigger').click()
  await expect(page.getByRole('dialog')).toBeVisible()
}

test('at 390x844 the filters are a dialog anchored to the bottom edge', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openFilters(page)

  const sheet = page.getByTestId('filter-sheet')
  await expect(sheet).toHaveAttribute('role', 'dialog')
  await expect(sheet).toHaveAttribute('aria-modal', 'true')
  await expect(sheet).toHaveAccessibleName('Filters')
  // Flush with the bottom edge and as wide as the screen: a sheet, not a centred card. Polled,
  // because the sheet slides up from below the edge as it opens; the claim is about where it
  // comes to rest, not where the first frame of its entrance happens to be.
  await expect
    .poll(async () => {
      const box = await sheet.boundingBox()
      return box === null ? null : [Math.round(box.y + box.height), Math.round(box.width)]
    })
    .toEqual([PHONE.height, PHONE.width])
})

test('at 1440x900 the filters are the centred dialog, not the sheet', async ({ page }) => {
  await page.setViewportSize(DESKTOP)
  await openFilters(page)

  await expect(page.getByTestId('filter-sheet')).toHaveCount(0)
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  const box = await dialog.boundingBox()
  expect(box).not.toBeNull()
  const centre = (box?.x ?? 0) + (box?.width ?? 0) / 2
  expect(Math.abs(centre - DESKTOP.width / 2)).toBeLessThan(2)
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThan(DESKTOP.height)
})

for (const viewport of [PHONE, DESKTOP]) {
  test(`Escape closes the filters and focus returns to the trigger at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await openFilters(page)

    // Focus moved into the dialog on open.
    await expect(page.getByRole('dialog').locator(':focus')).toHaveCount(1)
    await page.keyboard.press('Escape')

    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByTestId('filter-trigger')).toBeFocused()
  })
}

test('the backdrop closes the sheet without applying the draft', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openFilters(page)

  await page.getByRole('combobox', { name: 'Status' }).click()
  await page.getByRole('option', { name: 'suspended' }).click()
  // The top strip of the screen is backdrop, above the sheet.
  await page.getByTestId('filter-sheet-backdrop').click({ position: { x: 20, y: 20 } })

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByTestId('applied')).toHaveText('{"status":""}')
})

test('Apply commits the draft, and its chip removes it again', async ({ page }) => {
  await page.setViewportSize(PHONE)
  await openFilters(page)

  await page.getByRole('combobox', { name: 'Status' }).click()
  await page.getByRole('option', { name: 'active' }).click()
  await page.getByRole('button', { name: 'Apply' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByTestId('applied')).toHaveText('{"status":"active"}')
  await expect(page.getByTestId('filter-trigger-dot')).toBeVisible()

  await page.getByRole('button', { name: 'Remove filter: active' }).click()
  await expect(page.getByTestId('applied')).toHaveText('{"status":""}')
})

for (const locale of ['en', 'ru', 'hy']) {
  test(`the open sheet does not scroll sideways at 390px in ${locale}`, async ({ page }) => {
    await page.setViewportSize(PHONE)
    await openFilters(page, locale)

    const overflow = await page.evaluate(() => {
      const sheet = document.querySelector('[data-testid="filter-sheet"]')
      return {
        document: document.documentElement.scrollWidth - window.innerWidth,
        sheet: sheet === null ? -1 : sheet.scrollWidth - sheet.clientWidth,
      }
    })
    expect(overflow.document).toBeLessThanOrEqual(0)
    expect(overflow.sheet).toBe(0)
  })
}
