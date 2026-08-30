import { expect, test } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts, stubCreateAccountProblem } from '../fixtures/stub-accounts-route'
import type { PanelModule } from '../../src/types/module'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPlans, THE_PLAN } from '../fixtures/stub-plans-route'

/** Catalogue every spec here starts from: the accounts module licensed, so the form is reachable. */
const LICENSED_ACCOUNTS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

// rules/vue.md, "Forms: the browser never validates": UiForm renders
// `<form novalidate>` always, and no field may carry a native validation
// attribute — a browser bubble would appear in the BROWSER's language and
// short-circuit the panel's own (correctly localized) message.
test('the create-account form disables browser validation and uses no native validation attributes', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  await page.goto('/accounts/new')

  await expect(page.locator('form')).toHaveAttribute('novalidate', '')

  // Two text inputs now: the plan is a listbox, whose options come from the backend,
  // so there is no third field for a browser to validate.
  const fields = page.locator('form input')
  await expect(fields).toHaveCount(2)
  const nativeAttributes = await fields.evaluateAll((inputs) => {
    return inputs.flatMap((input) => {
      return ['required', 'pattern', 'min', 'max', 'minlength', 'maxlength'].filter((attribute) => {
        return input.hasAttribute(attribute)
      })
    })
  })
  expect(nativeAttributes).toEqual([])

  // Required-ness is announced to assistive technology instead, per the same rule.
  await expect(page.getByLabel('Name')).toHaveAttribute('aria-required', 'true')
})

test('submitting the empty form shows the panel own field messages and sends no request', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  /** Every create request the browser attempted, so the spec can assert none was made. */
  const createRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/accounts')) {
      createRequests.push(request.url())
    }
  })

  await page.goto('/accounts/new')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByText('Name is required.')).toBeVisible()
  await expect(page.getByText('Primary domain is required.')).toBeVisible()
  await expect(page.getByText('Plan ID is required.')).toBeVisible()
  await expect(page.getByLabel('Name')).toHaveAttribute('aria-invalid', 'true')
  expect(createRequests).toEqual([])
})

test('submitting a name the client rules reject shows the mirrored rule message', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  await page.goto('/accounts/new')
  await page.getByLabel('Name').fill('X')
  await page.getByLabel('Primary domain').fill('not-a-domain')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(
    page.getByText('Name must be a lowercase, Linux-username-safe identifier', { exact: false }),
  ).toBeVisible()
  await expect(page.getByText('Primary domain must be a valid domain name.')).toBeVisible()
})

// The client rules mirror the server's, but the server stays the authority: a
// name taken between validation and submit comes back as a problem+json body,
// and rules/vue.md requires the SPA to render that text unchanged.
test('a rejected create renders the backend RFC 7807 detail verbatim and stays on the form', async ({
  page,
}) => {
  await stubSignedIn(page)
  const backendDetail = 'An account named "alpha" already exists on this server.'
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubPlans(page)
  await stubCreateAccountProblem(page, backendDetail)

  await page.goto('/accounts/new')
  await page.getByLabel('Name').fill('alpha')
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan ID' }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByRole('status')).toHaveText(backendDetail)
  await expect(page).toHaveURL('/accounts/new')
})

test('a valid submission posts the typed values and returns to the list showing the new account', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  await page.goto('/accounts/new')
  await page.getByLabel('Name').fill('alpha')
  await page.getByLabel('Primary domain').fill('alpha.example.com')
  await page.getByRole('combobox', { name: 'Plan ID' }).click()
  await page.getByRole('option', { name: THE_PLAN.displayName, exact: false }).click()

  const [request] = await Promise.all([
    page.waitForRequest((candidate) => {
      return candidate.method() === 'POST' && candidate.url().includes('/api/v1/accounts')
    }),
    page.getByRole('button', { name: 'Create account' }).click(),
  ])

  expect(request.postDataJSON()).toEqual({
    name: 'alpha',
    primaryDomain: 'alpha.example.com',
    planId: '22222222-2222-2222-2222-222222222222',
  })
  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('cell', { name: 'alpha.example.com' })).toBeVisible()
})

test('cancelling the form returns to the list without creating anything', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_ACCOUNTS)
  await stubAccounts(page, [])
  await stubPlans(page)

  /** Every create request the browser attempted, so the spec can assert none was made. */
  const createRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/accounts')) {
      createRequests.push(request.url())
    }
  })

  await page.goto('/accounts/new')
  await page.getByLabel('Name').fill('alpha')
  await page.getByRole('button', { name: 'Cancel' }).click()

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByText('No accounts yet')).toBeVisible()
  expect(createRequests).toEqual([])
})
