import { expect, test } from '../fixtures/real-test'
import { chooseLanguage, signInAs } from '../fixtures/real-sign-in'
import { openDestination } from '../fixtures/real-sidebar-walk'

// State that must not travel between screens (issue #519, found by hand as #508 and #494).

test('a validation message does not follow the administrator to another screen or back', async ({ page }) => {
  await signInAs(page, 'administrator')
  await openDestination(page, 'Accounts')
  await page.getByRole('link', { name: 'New account' }).first().click()
  await page.getByRole('button', { name: 'Create account' }).click()
  // The control: the message was on screen, so its later absence means something.
  await expect(page.getByText('Username is required.')).toBeVisible()

  await openDestination(page, 'Accounts')
  await page.getByRole('link', { name: 'New account' }).first().click()

  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
  await expect(page.getByText('Username is required.')).toHaveCount(0)
})

test('a message drawn in English does not stay on screen after the language changes', async ({ page }) => {
  await signInAs(page, 'administrator')
  await openDestination(page, 'Accounts')
  await page.getByRole('link', { name: 'New account' }).first().click()
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText('Username is required.')).toBeVisible()

  await chooseLanguage(page, 'Русский')

  await expect(page.getByText('Username is required.')).toHaveCount(0)
})
