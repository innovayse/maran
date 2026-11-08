import { expect, test } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { signInAsPhpCustomer, stubPhpSettings } from '../fixtures/stub-php-settings-route'

// Issue #138: a customer sets the PHP limits of their account within the plan's bounds. The labels,
// units, bounds and zone names are the panel's; the SPA draws one control per setting by its kind,
// sends a FULL replacement (a blank setting is simply not sent) and renders the backend's refusal
// verbatim.

// The customer reaches the page by the menu, not by typing the address.
test('the customer opens the PHP settings from the Sites and domains menu', async ({ page }) => {
  await signInAsPhpCustomer(page)
  await stubPhpSettings(page)

  await page.goto('/')
  await openSection(page, 'Sites and domains')
  await mainNavigation(page).getByRole('link', { name: 'PHP settings' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'PHP settings' })).toBeVisible()
  await expect(page).toHaveURL('/sites/php-settings')
})

test('the page lists every setting by its kind, says how many sites it reaches and shows the plan bound', async ({
  page,
}) => {
  await signInAsPhpCustomer(page)
  const traffic = await stubPhpSettings(page)

  await page.goto('/sites/php-settings')

  await expect(page.getByText('These settings apply to all your sites on PHP: 2 sites.')).toBeVisible()
  // The unit is the panel's word, beside the label; the stored value fills the field.
  await expect(page.getByLabel('Memory limit (MiB)')).toHaveValue('512')
  await expect(page.getByLabel('Upload size (MiB)')).toHaveValue('32')
  await expect(page.getByLabel('Execution time (seconds)')).toHaveValue('')
  // A count has no unit, so the label carries none.
  await expect(page.getByRole('textbox', { name: 'Input variables' })).toHaveValue('2000')
  await expect(page.getByRole('combobox', { name: 'Timezone' })).toBeVisible()
  await expect(page.getByText('Your plan allows at most 64 MiB.')).toBeVisible()
  await expect(page.getByText('Your plan allows at most 3000.')).toBeVisible()
  expect(traffic.reads()).toBe(1)
})

test('a stored value above the plan is flagged in words and the value in effect is shown', async ({ page }) => {
  await signInAsPhpCustomer(page)
  await stubPhpSettings(page)

  await page.goto('/sites/php-settings')

  await expect(
    page.getByText("The value you saved is above your plan's maximum, so the plan's maximum of 256 MiB applies instead."),
  ).toBeVisible()
  await expect(page.getByText('In effect now: 256 MiB.')).toBeVisible()
})

test('editing values saves them all as a full replacement and confirms it', async ({ page }) => {
  await signInAsPhpCustomer(page)
  const traffic = await stubPhpSettings(page)

  await page.goto('/sites/php-settings')
  await page.getByLabel('Upload size (MiB)').fill('48')
  await page.getByLabel('Execution time (seconds)').fill('90')
  await page.getByRole('combobox', { name: 'Timezone' }).click()
  await page.getByRole('option', { name: 'Asia/Yerevan' }).click()
  await page.getByRole('button', { name: 'Save PHP settings' }).click()

  await expect.poll(traffic.writes).toEqual([
    {
      settings: [
        { name: 'memory_limit', value: '512' },
        { name: 'upload_max_filesize', value: '48' },
        { name: 'max_execution_time', value: '90' },
        { name: 'max_input_vars', value: '2000' },
        { name: 'date.timezone', value: 'Asia/Yerevan' },
      ],
    },
  ])
  await expect(page.getByText('PHP settings saved.')).toBeVisible()
  // The answer replaces the form: nothing exceeds the plan any more.
  await expect(page.getByText("The value you saved is above your plan's maximum", { exact: false })).toHaveCount(0)
})

test('resetting a setting to its default removes it from the saved set', async ({ page }) => {
  await signInAsPhpCustomer(page)
  const traffic = await stubPhpSettings(page)

  await page.goto('/sites/php-settings')
  // A setting that was never set has nothing to reset.
  await expect(page.getByRole('button', { name: 'Reset to default: Execution time' })).toBeDisabled()
  await page.getByRole('button', { name: 'Reset to default: Memory limit' }).click()
  await expect(page.getByLabel('Memory limit (MiB)')).toHaveValue('')
  await page.getByRole('button', { name: 'Save PHP settings' }).click()

  await expect.poll(traffic.writes).toEqual([
    {
      settings: [
        { name: 'upload_max_filesize', value: '32' },
        { name: 'max_input_vars', value: '2000' },
      ],
    },
  ])
})

test('a number field that is not a whole number is caught and nothing is sent', async ({ page }) => {
  await signInAsPhpCustomer(page)
  const traffic = await stubPhpSettings(page)

  await page.goto('/sites/php-settings')
  await page.getByLabel('Upload size (MiB)').fill('lots')
  await page.getByRole('button', { name: 'Save PHP settings' }).click()

  await expect(page.getByText('Enter a whole number.')).toBeVisible()
  await expect(page.getByLabel('Upload size (MiB)')).toHaveAttribute('aria-invalid', 'true')
  expect(traffic.writes()).toEqual([])
})

test('a refused save renders the backend sentence verbatim and keeps what was typed', async ({ page }) => {
  const detail = 'The memory limit may be at most 256 MiB on your plan.'
  await signInAsPhpCustomer(page)
  await stubPhpSettings(page, undefined, 2, detail)

  await page.goto('/sites/php-settings')
  await page.getByLabel('Memory limit (MiB)').fill('4096')
  await page.getByRole('button', { name: 'Save PHP settings' }).click()

  await expect(page.getByText(detail, { exact: true })).toBeVisible()
  await expect(page.getByLabel('Memory limit (MiB)')).toHaveValue('4096')
  await expect(page.getByText('PHP settings saved.')).toHaveCount(0)
})

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/sites/php-settings' }

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the PHP settings do not scroll sideways at 390px in ${locale}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale)
    await signInAsPhpCustomer(page)
    await stubPhpSettings(page)

    await page.goto(SWEPT_SCREEN.path)
    // Positive control: the settings are drawn before the page width is measured.
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByRole('textbox').first()).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
