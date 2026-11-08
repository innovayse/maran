import { expect, test } from '@playwright/test'
import { CUSTOMER, openDnsScreen } from '../fixtures/open-dns-screen'
import { DNS_SERVER, DNS_SETTINGS } from '../fixtures/stub-dns-routes'

// The name server settings (issue #178, administrators only): the name server's state and switch,
// and the values written into every zone. Turning the server on opens port 53, so it asks first.

test('the page shows the name server\'s state and the saved settings', async ({ page }) => {
  await openDnsScreen(page)

  await page.goto('/dns/nameservers')

  await expect(page.getByRole('heading', { level: 1, name: 'Nameservers' })).toBeVisible()
  await expect(page.getByText('Answering', { exact: true })).toBeVisible()
  await expect(page.getByText('9.18.28')).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Primary name server' })).toHaveValue('ns1.panel.example')
  await expect(page.getByRole('textbox', { name: 'Secondary name server' })).toHaveValue('ns2.panel.example')
  await expect(page.getByRole('textbox', { name: 'Hostmaster mailbox (SOA form)' })).toHaveValue('hostmaster.panel.example')
  await expect(page.getByRole('textbox', { name: 'Public IPv4 address' })).toHaveValue('203.0.113.10')
})

test('a server that is turned on but not answering is told apart from one that is', async ({ page }) => {
  await openDnsScreen(page, { server: { ...DNS_SERVER, running: false, listening: false } })

  await page.goto('/dns/nameservers')

  await expect(page.getByText('Not answering', { exact: true })).toBeVisible()
  await expect(page.getByText('Turned on')).toBeVisible()
})

test('a host the panel could not read says so instead of printing the facts as no', async ({ page }) => {
  await openDnsScreen(page, { server: { ...DNS_SERVER, agentReachable: false } })

  await page.goto('/dns/nameservers')

  await expect(page.getByText('The panel could not reach the agent on this host')).toBeVisible()
})

test('settings that are not complete are said to block creating zones', async ({ page }) => {
  await openDnsScreen(page, { settings: { ...DNS_SETTINGS, secondaryNameserver: '', complete: false } })

  await page.goto('/dns/nameservers')

  await expect(page.getByText('The settings are incomplete, so zones cannot be created yet.')).toBeVisible()
})

test('saving sends the settings an administrator edits and says so', async ({ page }) => {
  const state = await openDnsScreen(page)

  await page.goto('/dns/nameservers')
  await page.getByRole('textbox', { name: 'Primary name server' }).fill('ns1.example.net')
  await page.getByRole('textbox', { name: 'Secondary name server' }).fill('')
  await page.getByRole('textbox', { name: 'Public IPv6 address' }).fill('2001:db8::10')
  await page.getByRole('button', { name: 'Save settings' }).click()

  await expect(page.getByText('The settings were saved.')).toBeVisible()
  const put = state.requests.find((request) => {
    return request.method === 'PUT'
  })
  expect(put?.body).toEqual({
    primaryNameserver: 'ns1.example.net',
    secondaryNameserver: '',
    hostmasterMailbox: 'hostmaster.panel.example',
    publicIpv4: '203.0.113.10',
    publicIpv6: '2001:db8::10',
    defaultTtl: 3600,
  })
})

test('values the panel can reject are refused without asking the server', async ({ page }) => {
  const state = await openDnsScreen(page)

  await page.goto('/dns/nameservers')
  await page.getByRole('textbox', { name: 'Primary name server' }).fill('')
  await page.getByRole('textbox', { name: 'Hostmaster mailbox (SOA form)' }).fill('hostmaster@panel.example')
  await page.getByRole('textbox', { name: 'Public IPv4 address' }).fill('999.1.1.1')
  await page.getByRole('textbox', { name: 'Default TTL (seconds)' }).fill('5')
  await page.getByRole('button', { name: 'Save settings' }).click()

  await expect(page.getByText('Enter the primary name server.')).toBeVisible()
  await expect(page.getByText('Write the mailbox with a dot instead of the at sign, such as hostmaster.example.com.')).toBeVisible()
  await expect(page.getByText('Enter an IPv4 address such as 203.0.113.10.')).toBeVisible()
  await expect(page.getByText('The TTL must be between 60 and 604800 seconds.')).toBeVisible()
  expect(
    state.requests.some((request) => {
      return request.method === 'PUT'
    }),
  ).toBe(false)
})

test('a name that is not a host name is refused as a name server', async ({ page }) => {
  await openDnsScreen(page)

  await page.goto('/dns/nameservers')
  await page.getByRole('textbox', { name: 'Secondary name server' }).fill('bad name with spaces')
  await page.getByRole('button', { name: 'Save settings' }).click()

  await expect(page.getByText('A name server must be a host name such as ns1.example.net.')).toBeVisible()
})

test('the server\'s refusal of the settings is shown in its own words', async ({ page }) => {
  await openDnsScreen(page, {
    refusals: { 'PUT dns-settings': { detail: 'ns1.example.net does not resolve to this server.' } },
  })

  await page.goto('/dns/nameservers')
  await page.getByRole('button', { name: 'Save settings' }).click()

  await expect(page.getByText('ns1.example.net does not resolve to this server.')).toBeVisible()
})

test('turning the name server on asks first and says that it opens port 53', async ({ page }) => {
  const state = await openDnsScreen(page, {
    server: { ...DNS_SERVER, enabled: false, running: false, listening: false },
  })

  await page.goto('/dns/nameservers')
  await page.getByRole('button', { name: 'Turn on' }).click()

  const dialog = page.getByRole('dialog', { name: 'Turn the name server on?' })
  await expect(dialog).toContainText('opens port 53 to the internet')
  expect(
    state.requests.some((request) => {
      return request.url.pathname.endsWith('/enable')
    }),
  ).toBe(false)
  await dialog.getByRole('button', { name: 'Turn on' }).click()

  await expect(page.getByText('The name server was turned on.')).toBeVisible()
  await expect(page.getByText('Answering', { exact: true })).toBeVisible()
})

test('turning the name server off asks first and says that the zones are kept', async ({ page }) => {
  await openDnsScreen(page)

  await page.goto('/dns/nameservers')
  await page.getByRole('button', { name: 'Turn off' }).click()

  const dialog = page.getByRole('dialog', { name: 'Turn the name server off?' })
  await expect(dialog).toContainText('the zones are kept')
  await dialog.getByRole('button', { name: 'Turn off' }).click()

  await expect(page.getByText('The name server was turned off.')).toBeVisible()
  await expect(page.getByText('Not answering', { exact: true })).toBeVisible()
})

test('a refused switch keeps the state and shows the panel\'s reason', async ({ page }) => {
  await openDnsScreen(page, {
    server: { ...DNS_SERVER, enabled: false, running: false, listening: false },
    refusals: { 'POST dns-server/enable': { detail: 'Port 53 is already used by another service on this host.' } },
  })

  await page.goto('/dns/nameservers')
  await page.getByRole('button', { name: 'Turn on' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Turn on' }).click()

  await expect(page.getByText('Port 53 is already used by another service on this host.')).toBeVisible()
  await expect(page.getByText('Not answering', { exact: true })).toBeVisible()
})

test('a failed read shows the panel\'s reason and no form', async ({ page }) => {
  await openDnsScreen(page, {
    refusals: { 'GET dns-settings': { detail: 'The DNS module is switched off on this host.', status: 503 } },
  })

  await page.goto('/dns/nameservers')

  await expect(page.getByText('The DNS module is switched off on this host.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save settings' })).toHaveCount(0)
})

test('a customer who opens the address is turned away to the not-available screen', async ({ page }) => {
  await openDnsScreen(page, {}, CUSTOMER)

  await page.goto('/dns/nameservers')

  await expect(page.getByRole('heading', { level: 1, name: 'Nameservers' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Save settings' })).toHaveCount(0)
})
