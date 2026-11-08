import { expect, test } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { readPublishedLogin } from './sign-in-as'

// The public demo's sign-in screen (issue #95): the banner with the next reset, and the block that
// publishes both logins. Run only through playwright.demo.config.ts against a running demo.

declare const process: { readonly env: Readonly<Record<string, string | undefined>> }

test('the demo build is under test: a missing origin is a failure, never a skip', () => {
  expect(process.env.MARAN_DEMO_URL ?? '', 'MARAN_DEMO_URL is unset, so there is no demo to open').not.toBe('')
})

test('the sign-in screen shows the demo banner with the time of the next reset', async ({ page }) => {
  await page.goto('/login')

  const banner = page.getByTestId('demo-banner')
  await expect(banner).toBeVisible()
  // A formatted instant carries its year; "Next reset: ." with nothing after it would not.
  await expect(page.getByTestId('demo-next-reset')).toContainText(/\d{4}/)
})

test('the sign-in screen publishes an administrator and a customer login', async ({ page }) => {
  await page.goto('/login')

  const administrator = await readPublishedLogin(page, 'administrator')
  const customer = await readPublishedLogin(page, 'customer')
  expect(administrator.username, 'both roles print the same login name').not.toBe(customer.username)
})

test('choosing a published login fills the sign-in form without submitting it', async ({ page }) => {
  await page.goto('/login')
  const customer = await readPublishedLogin(page, 'customer')

  await page.getByTestId('demo-identity-customer').getByRole('button').click()

  await expect(page.locator('input[autocomplete="username"]')).toHaveValue(customer.username)
  await expect(page.locator('input[type="password"]')).toHaveValue(customer.password)
  await expect(page).toHaveURL(/\/login/)
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the sign-in screen with the banner and the logins does not scroll sideways at 390px in ${locale}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale)
    await page.goto('/login')
    await expect(page.getByTestId('demo-credentials')).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
