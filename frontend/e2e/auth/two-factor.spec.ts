import { expect, test } from '@playwright/test'
import { stubLogin, stubSignedOut, stubbedSignIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

/** What the backend answers a password-only sign-in with when the user has a second factor. */
const TWO_FACTOR_OWED = {
  accessToken: null,
  expiresAt: null,
  twoFactorRequired: true,
  user: null,
}

test('a user with a second factor is taken to the code screen rather than shown an error', async ({
  page,
}) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, TWO_FACTOR_OWED)

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Nothing has failed: the password was right and the sign-in is simply incomplete.
  await expect(page).toHaveURL('/login/two-factor')
  await expect(page.getByRole('textbox', { name: 'Authenticator code' })).toBeVisible()
})

test('entering the authenticator code completes the sign-in', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, TWO_FACTOR_OWED)
  await page.route('**/api/v1/auth/two-factor', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(stubbedSignIn),
    })
  })

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('textbox', { name: 'Authenticator code' }).fill('123456')
  await page.getByRole('button', { name: 'Verify' }).click()

  await expect(page).toHaveURL('/')
})

test('the field asks for a recovery code once the user says the authenticator is gone', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, TWO_FACTOR_OWED)

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('button', { name: 'Use a recovery code instead' }).click()

  // The label, not only a hint: what is being asked for has changed, and the field
  // must say so to anyone reading it through assistive technology too.
  await expect(page.getByRole('textbox', { name: 'Recovery code' })).toBeVisible()
})

test('a wrong code shows the backend message and keeps the user on the code screen', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubLogin(page, TWO_FACTOR_OWED)
  await page.route('**/api/v1/auth/two-factor', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'InvalidTwoFactorCodeUnauthorized',
        detail: 'That code is not valid. Check your authenticator app and try again.',
      }),
    })
  })

  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('textbox', { name: 'Authenticator code' }).fill('000000')
  await page.getByRole('button', { name: 'Verify' }).click()

  await expect(
    page.getByText('That code is not valid. Check your authenticator app and try again.'),
  ).toBeVisible()
  await expect(page).toHaveURL('/login/two-factor')
})

test('opening the code screen directly, without having passed the password step, returns to sign-in', async ({
  page,
}) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login/two-factor')

  // There is no username held and no password carried, so there is nothing here to finish.
  await expect(page).toHaveURL('/login')
})
