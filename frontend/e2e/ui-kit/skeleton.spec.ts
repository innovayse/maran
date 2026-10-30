import { earlySkeletons, recordSkeletonFlashes } from '../fixtures/skeleton-flash'
import { expect, test, type Page } from '@playwright/test'

// The loading kit through `e2e/fixtures/skeleton-harness.html` (see that file): placeholders drawn
// from the table's own column schema and the form's own fields, hidden from assistive technology,
// one polite status, no flash for a fast answer, and an error that is never a skeleton (#243).

/** Every column kind the table schema knows; the harness has one column of each. */
const KINDS = ['identity', 'identifier', 'text', 'mono', 'code', 'badge', 'number', 'date', 'actions'] as const

/**
 * Opens the harness.
 * @param page The Playwright page.
 * @param query The harness's query string.
 * @returns Resolves once the region is rendered.
 */
const openHarness = async (page: Page, query: string): Promise<void> => {
  await page.goto(`/e2e/fixtures/skeleton-harness.html?${query}`)
  await expect(page.getByTestId('loading-region')).toBeAttached()
}

test('every column kind draws its placeholder under the real header', async ({ page }) => {
  await openHarness(page, 'delay=never')
  const table = page.getByRole('table', { name: 'Fixture table' })

  await expect(table.getByRole('columnheader', { name: 'Identity' })).toBeVisible()
  await expect(table.getByRole('columnheader', { name: 'Actions' })).toBeVisible()
  for (const kind of KINDS) {
    await expect(page.locator(`[data-skeleton="cell-${kind}"]`)).toHaveCount(3)
    await expect(page.locator(`[data-skeleton="cell-${kind}"]`).first()).toBeVisible()
  }
  await expect(table).toHaveAttribute('aria-busy', 'true')
})

test('the form and the description list keep their real labels while loading', async ({ page }) => {
  await openHarness(page, 'delay=never')

  const labels = ['Input label', 'Select label', 'Textarea label', 'Combobox label', 'Switch label', 'Checkbox label']
  for (const label of labels) {
    await expect(page.getByText(label, { exact: true })).toBeVisible()
  }
  await expect(page.getByText('Term label', { exact: true })).toBeVisible()
  await expect(page.getByText('Term value', { exact: true })).toHaveCount(0)
  // The controls are not there to be used yet: each is a placeholder of its footprint.
  await expect(page.getByRole('textbox')).toHaveCount(0)
  await expect(page.getByRole('switch')).toHaveCount(0)
  await expect(page.getByRole('checkbox')).toHaveCount(0)
  await expect(page.locator('[data-skeleton="block"]')).toHaveCount(6)
  await expect(page.locator('[data-skeleton="kpi"]')).toBeVisible()
  await expect(page.locator('[data-skeleton="card"]')).toBeVisible()
})

test('every placeholder is hidden from assistive technology', async ({ page }) => {
  await openHarness(page, 'delay=never')
  const placeholders = page.locator('[data-skeleton]')

  expect(await placeholders.count()).toBeGreaterThan(0)
  const exposed = await placeholders.evaluateAll((elements) => {
    return elements.filter((element) => {
      return element.closest('[aria-hidden="true"]') === null
    }).length
  })
  expect(exposed).toBe(0)
})

test('one polite status says loading, then loaded', async ({ page }) => {
  await openHarness(page, 'delay=600')
  const status = page.getByTestId('loading-status')

  await expect(status).toHaveAttribute('role', 'status')
  await expect(status).toHaveAttribute('aria-live', 'polite')
  await expect(status).toHaveText('Loading…')
  await expect(page.getByTestId('loading-region')).toHaveAttribute('aria-busy', 'true')

  await expect(status).toHaveText('Loaded')
  await expect(page.getByTestId('loading-region')).not.toHaveAttribute('aria-busy', 'true')
  await expect(page.getByTestId('cell-identity').first()).toHaveText('alpha')
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByRole('status')).toHaveCount(1)
})

test('the status is translated', async ({ page }) => {
  await openHarness(page, 'delay=never&locale=hy')
  await expect(page.getByTestId('loading-status')).toHaveText('Բեռնվում է…')
})

test('a reader who asked for less motion gets no shimmer', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await openHarness(page, 'delay=never')

  const animation = await page.locator('[data-skeleton="cell-text"]').first().evaluate((element) => {
    return getComputedStyle(element).animationName
  })
  expect(animation).toBe('none')
})

test('without the preference the placeholder shimmers', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await openHarness(page, 'delay=never')

  const animation = await page.locator('[data-skeleton="cell-text"]').first().evaluate((element) => {
    return getComputedStyle(element).animationName
  })
  expect(animation).not.toBe('none')
})

test('a 50 ms answer shows no placeholder', async ({ page }) => {
  await recordSkeletonFlashes(page)
  await openHarness(page, 'delay=50')

  // Every frame from the first, for longer than the answer and the 150 ms delay together: no
  // placeholder may be seen in the first 150 ms of its wait, although they are mounted to hold
  // the space (see `recordSkeletonFlashes` for why the wait, not the stub, is the clock).
  await page.waitForTimeout(400)
  expect(await earlySkeletons(page)).toEqual([])
  await expect(page.getByTestId('loading-status')).toHaveText('Loaded')
})

test('a failed load shows the alert and never a skeleton', async ({ page }) => {
  await openHarness(page, 'delay=300&outcome=error')

  await expect(page.getByText('The fixture failed to load.')).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByTestId('loading-region')).not.toHaveAttribute('aria-busy', 'true')
})
