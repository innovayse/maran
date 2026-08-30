import { expect, test } from '@playwright/test'
import { stubSetupState } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

test('a panel with no administrator sends every route to the setup screen', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/accounts')

  await expect(page).toHaveURL('/setup')
  await expect(page.getByRole('heading', { level: 2, name: 'Set up this panel' })).toBeVisible()
})

test('the setup screen prefills the token from the installer link', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup?token=one-time-token')

  await expect(page.getByRole('textbox', { name: 'Setup token' })).toHaveValue('one-time-token')
})

test('a mismatched confirmation is reported on the field before anything is sent', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('correct horse battery staple')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('correct horse battery stapl')

  await expect(page.getByText('The passwords do not match.')).toBeVisible()
})

test('creating the administrator lands on the sign-in screen', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/setup', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: '00000000-0000-0000-0000-000000000001',
        username: 'admin',
        email: 'admin@example.com',
        role: 'admin',
        accountId: null,
      }),
    })
  })

  await page.goto('/setup?token=one-time-token')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Email' }).fill('admin@example.com')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('correct horse battery staple')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Create administrator' }).click()

  // Deliberately not signed in automatically: typing the new password once proves
  // it is the one the operator meant to set.
  await expect(page).toHaveURL('/login')
})

test('a rejected password shows the rule the backend broke it on', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/setup', async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'PasswordTooWeak',
        detail: 'Choose a password of at least 12 characters that is different from your username.',
      }),
    })
  })

  await page.goto('/setup?token=one-time-token')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Email' }).fill('admin@example.com')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('short')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('short')
  await page.getByRole('button', { name: 'Create administrator' }).click()

  await expect(
    page.getByText('Choose a password of at least 12 characters that is different from your username.'),
  ).toBeVisible()
})
