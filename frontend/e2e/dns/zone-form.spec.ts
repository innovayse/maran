import { expect, test } from '../fixtures/guarded-test'
import { ALICE, CUSTOMER, openDnsScreen } from '../fixtures/open-dns-screen'
import { WEB_AND_MAIL_TEMPLATE, WEB_TEMPLATE, openDnsTemplatesScreen } from '../fixtures/stub-dns-templates-routes'

// The create-zone form (issue #178). Its checks are advice that saves a round trip; the server
// decides which names an account may create (a name one of its sites claims) and its refusal is
// shown as it was sent.

/**
 * Counts the create requests the page has sent.
 * @param requests Every request the stub saw.
 * @returns How many were `POST` calls to the zones collection.
 */
const creates = (requests: { method: string; url: URL }[]): number => {
  return requests.filter((request) => {
    return request.method === 'POST' && request.url.pathname.endsWith('/dns-zones')
  }).length
}

test('an administrator creates a zone for an account and lands on the new zone', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] })

  await page.goto('/dns/zones/new')
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Domain name' }).fill('example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page).toHaveURL(/\/dns\/zones\/99999999-9999-9999-9999-999999999999$/)
  await expect(page.getByText('The zone example.com was created.')).toBeVisible()
  const created = state.requests.find((request) => {
    return request.method === 'POST'
  })
  expect(created?.body).toEqual({ accountId: ALICE.id, name: 'example.com', templateId: null })
})

test('a customer creates a zone without choosing an account', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')

  await expect(page.getByRole('combobox', { name: 'Account' })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Domain name' }).fill('alice.example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page).toHaveURL(/\/dns\/zones\/99999999/)
  const created = state.requests.find((request) => {
    return request.method === 'POST'
  })
  expect(created?.body).toEqual({ accountId: ALICE.id, name: 'alice.example.com', templateId: null })
})

test('a name that cannot be a zone is refused without asking the server', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')
  await page.getByRole('textbox', { name: 'Domain name' }).fill('localhost')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page.getByText('Enter a domain name of two or more parts, such as example.com.')).toBeVisible()
  expect(creates(state.requests)).toBe(0)
})

test('an empty name is refused without asking the server', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page.getByText('Enter the domain name of the zone.')).toBeVisible()
  expect(creates(state.requests)).toBe(0)
})

test('the administrator must choose an account before the form asks the server', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] })

  await page.goto('/dns/zones/new')
  await page.getByRole('textbox', { name: 'Domain name' }).fill('example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page.getByText('Choose the account that owns the zone.')).toBeVisible()
  expect(creates(state.requests)).toBe(0)
})

test('the server\'s refusal is shown above the form in its own words and the form stays', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/dns-zones$/)
  await openDnsScreen(
    page,
    {
      zones: [],
      refusals: { 'POST dns-zones': { detail: 'No site of this account serves shop.example.net.' } },
    },
    CUSTOMER,
  )

  await page.goto('/dns/zones/new')
  await page.getByRole('textbox', { name: 'Domain name' }).fill('shop.example.net')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page.getByText('No site of this account serves shop.example.net.')).toBeVisible()
  await expect(page).toHaveURL(/\/dns\/zones\/new$/)
  await expect(page.getByRole('textbox', { name: 'Domain name' })).toHaveValue('shop.example.net')
})

test('cancel returns to the list without sending anything', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')
  await page.getByRole('button', { name: 'Cancel' }).click()

  await expect(page).toHaveURL(/\/dns\/zones$/)
  expect(creates(state.requests)).toBe(0)
})

// The template a zone starts from (issue #452). The choices come from the panel (names only, never
// records), the default one is preselected, and "empty zone" is always offered.

test('the default template is preselected and its identity is sent with the new zone', async ({ page }) => {
  const { dns } = await openDnsTemplatesScreen(page, { templates: [WEB_TEMPLATE, WEB_AND_MAIL_TEMPLATE] }, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')

  const select = page.getByRole('combobox', { name: 'Start from' })
  await expect(select).toContainText('web')
  await page.getByRole('textbox', { name: 'Domain name' }).fill('alice.example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page).toHaveURL(/\/dns\/zones\/99999999/)
  const created = dns.requests.find((request) => {
    return request.method === 'POST'
  })
  expect(created?.body).toEqual({ accountId: ALICE.id, name: 'alice.example.com', templateId: WEB_TEMPLATE.id })
})

test('choosing another template sends that template, and the empty zone sends none', async ({ page }) => {
  const { dns } = await openDnsTemplatesScreen(page, { templates: [WEB_TEMPLATE, WEB_AND_MAIL_TEMPLATE] }, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')
  await expect(page.getByRole('combobox', { name: 'Start from' })).toContainText('web')
  await page.getByRole('combobox', { name: 'Start from' }).click()
  await page.getByRole('option', { name: 'web and mail' }).click()
  await page.getByRole('textbox', { name: 'Domain name' }).fill('alice.example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()
  await expect(page).toHaveURL(/\/dns\/zones\/99999999/)

  await page.goto('/dns/zones/new')
  await page.getByRole('combobox', { name: 'Start from' }).click()
  await page.getByRole('option', { name: 'Empty zone' }).click()
  await page.getByRole('textbox', { name: 'Domain name' }).fill('alice.example.org')
  await page.getByRole('button', { name: 'Create zone' }).click()
  await expect(page).toHaveURL(/\/dns\/zones\/99999999/)

  const bodies = dns.requests
    .filter((request) => {
      return request.method === 'POST'
    })
    .map((request) => {
      return (request.body as { templateId: string | null }).templateId
    })
  expect(bodies).toEqual([WEB_AND_MAIL_TEMPLATE.id, null])
})

test('with no default template the form starts from an empty zone', async ({ page }) => {
  await openDnsTemplatesScreen(page, { templates: [{ ...WEB_AND_MAIL_TEMPLATE, isDefault: false }] }, { zones: [] }, CUSTOMER)

  await page.goto('/dns/zones/new')

  await expect(page.getByRole('combobox', { name: 'Start from' })).toContainText('Empty zone')
})

test('the panel refusing the template is shown above the form and the form stays', async ({ page, guard }) => {
  guard.allow(404, /\/api\/v1\/dns-zones$/)
  await openDnsTemplatesScreen(
    page,
    { templates: [WEB_TEMPLATE] },
    { zones: [], refusals: { 'POST dns-zones': { detail: 'The template no longer exists.', status: 404 } } },
    CUSTOMER,
  )

  await page.goto('/dns/zones/new')
  await expect(page.getByRole('combobox', { name: 'Start from' })).toContainText('web')
  await page.getByRole('textbox', { name: 'Domain name' }).fill('alice.example.com')
  await page.getByRole('button', { name: 'Create zone' }).click()

  await expect(page.getByText('The template no longer exists.')).toBeVisible()
  await expect(page).toHaveURL(/\/dns\/zones\/new$/)
})
