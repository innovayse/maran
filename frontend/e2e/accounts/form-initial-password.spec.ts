import { expect, test, type Page } from '@playwright/test'
import { stubCreateRefused, recordCreateRequests, submitCreate } from '../fixtures/stub-account-create'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubPlans, THE_PLAN } from '../fixtures/stub-plans-route'
import { setPersistedLocale } from '../fixtures/set-locale'

// Issue #194: the owner's login is made by an invitation (the default) or by an initial password
// the operator types or generates. The password is required in that mode, is sent only then, and
// the panel says the owner must replace it at the first sign-in.

/**
 * Signs in as an administrator and opens the create form, in whatever language the page was seeded with.
 * @param page The Playwright page.
 * @returns Resolves once the form is on screen.
 */
const openForm = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubAccounts(page, [])
  await stubPlans(page)
  // The create is refused so the page stays and the sent body can be read.
  await stubCreateRefused(page)
  await page.goto('/accounts/new')
  await expect(page.getByRole('radio').first()).toBeVisible()
}

/**
 * Opens the create form and fills the fields every account needs.
 * @param page The Playwright page.
 * @returns Resolves once the form is filled.
 */
const openFilledForm = async (page: Page): Promise<void> => {
  await openForm(page)
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan', exact: true }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()
  await page.getByLabel('Owner email').fill('owner@alpha.example.com')
}

test('the invitation is the default: no password field and no password in the body', async ({ page }) => {
  await openFilledForm(page)

  await expect(page.getByRole('radio', { name: 'Mail an invitation' })).toBeChecked()
  await expect(page.getByLabel('Initial password', { exact: true })).toHaveCount(0)
  const request = await submitCreate(page)

  expect(request.postDataJSON()).not.toHaveProperty('initialPassword')
  expect(request.postDataJSON()).not.toHaveProperty('loginMode')
})

test('choosing an initial password asks for one and says the owner must replace it', async ({ page }) => {
  await openFilledForm(page)
  const createRequests = recordCreateRequests(page)

  await page.getByRole('radio', { name: 'Set an initial password' }).check({ force: true })
  await expect(page.getByText('must replace this password at the first sign-in', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('Enter an initial password or generate one.')).toBeVisible()
  await expect(page.getByLabel('Initial password', { exact: true })).toHaveAttribute('aria-invalid', 'true')
  expect(createRequests).toEqual([])
})

test('a generated password fills the field and is sent with the initial-password mode', async ({ page }) => {
  await openFilledForm(page)
  await page.getByRole('radio', { name: 'Set an initial password' }).check({ force: true })

  await page.getByRole('button', { name: /generate/i }).click()
  const generated = await page.getByLabel('Initial password', { exact: true }).inputValue()
  expect(generated.length).toBeGreaterThanOrEqual(16)
  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({ loginMode: 'initialPassword', initialPassword: generated })
})

test('a typed password is sent, and a refusal leaves it for the corrected resubmit', async ({ page }) => {
  await openFilledForm(page)
  await page.getByRole('radio', { name: 'Set an initial password' }).check({ force: true })
  await page.getByLabel('Initial password', { exact: true }).fill('Correct-Horse-9-Battery')

  const request = await submitCreate(page)

  expect(request.postDataJSON()).toMatchObject({
    loginMode: 'initialPassword',
    initialPassword: 'Correct-Horse-9-Battery',
  })
  await expect(page.getByText('Stop here.')).toBeVisible()
  await expect(page.getByLabel('Initial password', { exact: true })).toHaveValue('Correct-Horse-9-Battery')
})

test('the panel beside the form stops promising a mailed invitation when a password is set', async ({ page }) => {
  await openFilledForm(page)
  await expect(page.getByText('an invitation mailed to the owner email', { exact: false })).toBeVisible()

  await page.getByRole('radio', { name: 'Set an initial password' }).check({ force: true })

  await expect(page.getByText('no mail is sent', { exact: false })).toBeVisible()
  await expect(page.getByText('an invitation mailed to the owner email', { exact: false })).toHaveCount(0)
})

test('the choice is offered in Russian', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openForm(page)
  await expect(page.getByRole('radio', { name: 'Отправить приглашение' })).toBeChecked()
  await page.getByRole('radio', { name: 'Задать начальный пароль' }).check({ force: true })
  await expect(page.getByLabel('Начальный пароль', { exact: true })).toBeVisible()
})

test('the choice is offered in Armenian', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await openForm(page)
  await page.getByRole('radio', { name: 'Սահմանել սկզբնական գաղտնաբառ' }).check({ force: true })
  await expect(page.getByLabel('Սկզբնական գաղտնաբառ', { exact: true })).toBeVisible()
})
