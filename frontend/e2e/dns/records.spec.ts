import { expect, test, type Page } from '@playwright/test'
import { EXAMPLE_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'
import { dnsDraft, dnsRecord, type DnsStubState } from '../fixtures/stub-dns-routes'
import type { DnsRecordRequest } from '../../src/types/dnsRecord'

// The records pane of a zone (issue #178): a server-paged list with search, a filter sheet, the
// add and change dialog whose fields come from the panel's record type catalogue, a confirmed
// delete, and read-only rows for records the panel manages.

const WWW = dnsRecord(EXAMPLE_ZONE.id, 'rec-www', dnsDraft({ name: 'www', type: 'a', value: '203.0.113.10' }))

const MAIL = dnsRecord(EXAMPLE_ZONE.id, 'rec-mx', dnsDraft({ name: '@', type: 'mx', value: 'mail.example.com', priority: 10 }))

const MANAGED = dnsRecord(
  EXAMPLE_ZONE.id,
  'rec-ns',
  dnsDraft({ name: '@', type: 'ns', value: 'ns1.panel.example', ttl: 86400 }),
  'system',
)

/**
 * Opens the records pane of the example zone.
 * @param page The Playwright page under test.
 * @param extra Anything to add to the seed, such as refusals.
 * @returns The stub's live state.
 */
const openRecords = async (page: Page, extra: Partial<DnsStubState> = {}): Promise<DnsStubState> => {
  const state = await openDnsScreen(page, {
    zones: [EXAMPLE_ZONE],
    records: { [EXAMPLE_ZONE.id]: [WWW, MAIL, MANAGED] },
    ...extra,
  })
  await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)
  await page.getByRole('button', { name: 'Records', exact: true }).click()
  return state
}

/**
 * The record the stub holds last in the example zone.
 * @param state The stub's live state.
 * @returns The record, or `undefined` when the zone holds none.
 */
const lastRecord = (state: DnsStubState): DnsRecordRequest | undefined => {
  return state.records[EXAMPLE_ZONE.id]?.at(-1)
}

test('the records pane lists each record with its type, TTL, data and source', async ({ page }) => {
  await openRecords(page)

  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'www' })).toContainText('203.0.113.10')
  await expect(rows.filter({ hasText: 'www' })).toContainText('Added by hand')
  // The type is written the way the catalogue labels it, and the MX preference comes before the
  // target, the way a zone file reads it.
  await expect(rows.filter({ hasText: 'MX' })).toContainText('10 mail.example.com')
  await expect(rows.filter({ hasText: 'ns1.panel.example' })).toContainText('Managed by the panel')
})

test('a record the panel manages has no menu and says so', async ({ page }) => {
  await openRecords(page)

  const managed = page.getByRole('row').filter({ hasText: 'ns1.panel.example' })
  await expect(managed.getByRole('button')).toHaveCount(0)
  await expect(managed).toContainText('Managed by the panel')
  // The control: an editable record does have its menu.
  await expect(page.getByRole('button', { name: 'Actions for the A record www' })).toBeVisible()
})

test('a zone with no records shows the empty state and still offers to add one', async ({ page }) => {
  await openRecords(page, { records: { [EXAMPLE_ZONE.id]: [] } })

  await expect(page.getByText('No records yet')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add record' })).toBeEnabled()
})

test('the search is sent to the panel and a miss is the no-results state', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('searchbox', { name: 'Search records' }).fill('mail')
  await expect(page).toHaveURL(/search=mail/)
  await expect(page.getByRole('row').filter({ hasText: 'mail.example.com' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toHaveCount(0)
  expect(
    state.requests.some((request) => {
      return request.url.pathname.endsWith('/records') && request.url.searchParams.get('search') === 'mail'
    }),
  ).toBe(true)

  await page.getByRole('searchbox', { name: 'Search records' }).fill('nowhere')
  await expect(page.getByText('No records match')).toBeVisible()
  await expect(page.getByText('No records yet')).toHaveCount(0)
})

test('the filter sheet narrows by type with a removable chip', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'MX' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/type=mx/)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Type: MX')
  await expect(page.getByRole('row').filter({ hasText: 'mail.example.com' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toHaveCount(0)
  expect(
    state.requests.some((request) => {
      return request.url.searchParams.get('type') === 'mx'
    }),
  ).toBe(true)

  await page.getByRole('button', { name: 'Remove filter: Type: MX' }).click()
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toBeVisible()
})

test('adding an A record sends it, with zero for every input the type does not take', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('blog')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('203.0.113.77')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(page.getByText('The record was added.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'blog' })).toContainText('203.0.113.77')
  expect(lastRecord(state)).toMatchObject({
    name: 'blog',
    type: 'a',
    ttl: 3600,
    value: '203.0.113.77',
    priority: 0,
    weight: 0,
    port: 0,
    flags: 0,
    tag: '',
  })
})

test('the form draws the extra inputs the catalogue lists for the chosen type', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  // An A record takes only a value, so there is no priority to type.
  await expect(dialog.getByRole('textbox', { name: 'Priority' })).toHaveCount(0)

  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'SRV' }).click()
  await expect(dialog.getByRole('textbox', { name: 'Priority' })).toBeVisible()
  await expect(dialog.getByRole('textbox', { name: 'Weight' })).toBeVisible()
  await expect(dialog.getByRole('textbox', { name: 'Port' })).toBeVisible()
  await expect(dialog.getByText('Locates a service by host and port.')).toBeVisible()

  await dialog.getByRole('textbox', { name: 'Name' }).fill('_sip._tcp')
  await dialog.getByRole('textbox', { name: 'Priority' }).fill('10')
  await dialog.getByRole('textbox', { name: 'Weight' }).fill('5')
  await dialog.getByRole('textbox', { name: 'Port' }).fill('5060')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('sip.example.com')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(page.getByText('The record was added.')).toBeVisible()
  expect(lastRecord(state)).toMatchObject({ type: 'srv', priority: 10, weight: 5, port: 5060, flags: 0, tag: '' })
  await expect(page.getByRole('row').filter({ hasText: '_sip._tcp' })).toContainText('10 5 5060 sip.example.com')
})

test('a CAA record asks for a flag and a tag from the panel\'s list', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'CAA' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('@')
  await dialog.getByRole('textbox', { name: 'Flags' }).fill('0')
  await dialog.getByRole('combobox', { name: 'Tag' }).click()
  await page.getByRole('option', { name: 'issue', exact: true }).click()
  await dialog.getByRole('textbox', { name: 'Value' }).fill('letsencrypt.org')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(page.getByText('The record was added.')).toBeVisible()
  expect(lastRecord(state)).toMatchObject({ type: 'caa', flags: 0, tag: 'issue', value: 'letsencrypt.org' })
})

test('a missing priority is refused without asking the server', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'MX' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('@')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('mail.example.com')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(dialog.getByText('Enter a whole number from 0 to 65535.')).toBeVisible()
  expect(
    state.requests.some((request) => {
      return request.method === 'POST' && request.url.pathname.endsWith('/records')
    }),
  ).toBe(false)
})

test('a TTL outside the panel\'s bounds is refused with the bounds in the message', async ({ page }) => {
  await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('blog')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('203.0.113.77')
  await dialog.getByRole('textbox', { name: 'TTL (seconds)' }).fill('5')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(dialog.getByText('The TTL must be between 60 and 604800 seconds.')).toBeVisible()
})

test('a name the zone cannot hold is refused without asking the server', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('bad name!')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('203.0.113.77')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(dialog.getByText('Use letters, digits, hyphens and underscores in dot-separated parts.')).toBeVisible()
  expect(
    state.requests.some((request) => {
      return request.method === 'POST' && request.url.pathname.endsWith('/records')
    }),
  ).toBe(false)
})

test('a value with a control character is refused without asking the server', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'TXT' }).click()
  await dialog.getByRole('textbox', { name: 'Name' }).fill('@')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('v=spf1\u0007 -all')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(dialog.getByText('A value cannot contain line breaks or other control characters.')).toBeVisible()
  expect(
    state.requests.some((request) => {
      return request.method === 'POST' && request.url.pathname.endsWith('/records')
    }),
  ).toBe(false)
})

test('the server judges what a value must look like per type and its reason is shown in the dialog', async ({ page }) => {
  await openRecords(page, {
    refusals: { 'POST dns-zones/:id/records': { detail: 'An A record needs an IPv4 address.' } },
  })

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('blog')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('not an address')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(dialog.getByText('An A record needs an IPv4 address.')).toBeVisible()
  await expect(dialog).toBeVisible()
})

test('changing a record starts from its values and replaces it', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Actions for the A record www' }).click()
  await page.getByRole('menuitem', { name: 'Edit' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change a record' })
  await expect(dialog.getByRole('textbox', { name: 'Name' })).toHaveValue('www')
  await expect(dialog.getByRole('textbox', { name: 'Value' })).toHaveValue('203.0.113.10')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('203.0.113.99')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(page.getByText('The record was saved.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'www' })).toContainText('203.0.113.99')
  const put = state.requests.find((request) => {
    return request.method === 'PUT'
  })
  expect(put?.url.pathname).toMatch(/\/records\/rec-www$/)
})

test('changing an MX record shows its preference, and a preference of zero is kept', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Actions for the MX record @' }).click()
  await page.getByRole('menuitem', { name: 'Edit' }).click()
  const dialog = page.getByRole('dialog', { name: 'Change a record' })
  await expect(dialog.getByRole('textbox', { name: 'Priority' })).toHaveValue('10')
  await dialog.getByRole('textbox', { name: 'Priority' }).fill('0')
  await dialog.getByRole('button', { name: 'Save record' }).click()

  await expect(page.getByText('The record was saved.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'mail.example.com' })).toContainText('0 mail.example.com')
  const mx = state.records[EXAMPLE_ZONE.id]?.find((record) => {
    return record.id === 'rec-mx'
  })
  expect(mx).toMatchObject({ priority: 0 })
})

test('deleting a record asks first and removes it', async ({ page }) => {
  const state = await openRecords(page)

  await page.getByRole('button', { name: 'Actions for the A record www' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  const dialog = page.getByRole('dialog', { name: 'Delete the A record www?' })
  expect(
    state.requests.some((request) => {
      return request.method === 'DELETE'
    }),
  ).toBe(false)
  await dialog.getByRole('button', { name: 'Delete record' }).click()

  await expect(page.getByText('The record was deleted.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toHaveCount(0)
})

test('a refused delete keeps the record and shows the panel\'s reason', async ({ page }) => {
  await openRecords(page, {
    refusals: { 'DELETE dns-zones/:id/records/:id': { detail: 'The zone is being applied. Try again in a moment.' } },
  })

  await page.getByRole('button', { name: 'Actions for the A record www' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete record' }).click()

  await expect(page.getByText('The zone is being applied. Try again in a moment.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.10' })).toBeVisible()
})

test('the overview figures follow a change made in the records pane', async ({ page }) => {
  const state = await openRecords(page)
  const zone = state.zones[0]
  if (zone === undefined) {
    throw new Error('the seed holds the example zone')
  }

  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('blog')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('203.0.113.77')
  // The panel counts the new record and moves the serial before it answers the next read.
  zone.recordCount = 4
  zone.serial = 2026110702
  await dialog.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('The record was added.')).toBeVisible()

  await page.getByRole('button', { name: 'Overview', exact: true }).click()
  await expect(page.getByText('2026110702')).toBeVisible()
})
