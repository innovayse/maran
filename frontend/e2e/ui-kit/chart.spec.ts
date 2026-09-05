import { expect, test } from '@playwright/test'

// `UiChart` (src/components/ui/UiChart.vue) has no consuming page yet — the monitoring screen
// that will actually render it is a separate, later task carved out of the same plan, gated on
// backend modules that do not exist yet. These specs exercise the component directly through
// `e2e/fixtures/chart-harness.html`, a Playwright-only mount point that bypasses the app's
// router/shell/auth entirely (see that file's own comment for why).
//
// The harness's stubbed buckets are fixed UTC instants (`Date.UTC(2026, 0, 1, ...)`), and the
// component formats them through `new Date(at)`, which reads the BROWSER's local timezone. Pinned
// to UTC here so the hover readout's formatted timestamp is the same string on every machine and
// in CI, rather than shifting with whichever timezone happens to run the suite.
test.use({ timezoneId: 'UTC' })

test('chart renders the stubbed buckets as a line and a filled area, with no NaN in either path', async ({
  page,
}) => {
  await page.goto('/e2e/fixtures/chart-harness.html?scenario=populated')

  // A well-formed path is `M<x>,<y>` followed by one or more `L<x>,<y>` segments — this shape is
  // impossible to match if any coordinate came out `NaN`, so the regex doubles as the invariant
  // the plan calls out explicitly: a real series never produces a broken path.
  const line = page.locator('.ui-chart-line')
  await expect(line).toBeVisible()
  await expect(line).toHaveAttribute('d', /^M-?[\d.]+,-?[\d.]+(?: L-?[\d.]+,-?[\d.]+)+$/)

  const area = page.locator('.ui-chart-area')
  await expect(area).toBeVisible()
  await expect(area).toHaveAttribute('d', /^M-?[\d.]+,-?[\d.]+(?: L-?[\d.]+,-?[\d.]+)+ Z$/)
})

test("hovering the chart shows a readout formatted through the caller's formatValue", async ({ page }) => {
  await page.goto('/e2e/fixtures/chart-harness.html?scenario=populated')

  // The harness's third stubbed bucket (value 72) sits at the series' exact horizontal midpoint,
  // so hovering the chart's own bounding-box centre deterministically lands on it. The harness's
  // `formatValue` renders two decimal places, unlike the component's own one-decimal fallback —
  // proof the readout goes through the caller's formatter rather than the built-in default.
  await page.locator('svg').hover()

  await expect(page.locator('.ui-chart-readout-value')).toHaveText('72.00 custom-unit')
  await expect(page.locator('.ui-chart-readout-at')).toHaveText('1 Jan, 02:00')
})

test('an empty series renders the empty state, never an SVG with nothing to plot', async ({ page }) => {
  await page.goto('/e2e/fixtures/chart-harness.html?scenario=empty')

  await expect(page.getByText('CPU')).toBeVisible()
  await expect(page.getByText('No data recorded for this period yet.')).toBeVisible()
  // `UiEmptyState`'s icon slot renders a lucide `<svg>` of its own (the decorative "pulse"
  // glyph), so the chart's OWN plotting surface is what must be absent, not every `<svg>`.
  await expect(page.locator('svg.ui-chart-svg')).toHaveCount(0)
})

test('a single-point series draws one marker and no line, rather than a degenerate path', async ({ page }) => {
  await page.goto('/e2e/fixtures/chart-harness.html?scenario=single')

  await expect(page.locator('.ui-chart-marker')).toBeVisible()
  await expect(page.locator('.ui-chart-line')).toHaveCount(0)
  await expect(page.locator('.ui-chart-area')).toHaveCount(0)
})
