import { expect, test } from '../fixtures/guarded-test'
import {
  CHANGED_SERVER,
  FRESH_SERVER,
  MAIL_WARNING,
  OTHER_FINGERPRINT,
  PINNED_SERVER,
  PRIVATE_KEY_MARKER,
  READY_SERVER,
  SCANNED_KEYS,
  discoveredAccount,
  openMigrationScreen,
  openRemoteServersList,
} from '../fixtures/migration'
import type { RemoteServerDetail } from '../../src/types/migrationSource'
import type { Page } from '@playwright/test'

// Server migration, phase 1 (issue #197): the remote server registry and the discovery of the
// accounts on a source. Every screen is reached by clicking, from the sidebar down; the stub holds
// the panel's state, so a spec reads back what the page sent. The SPA's checks are advice (the
// panel enforces), so each refusing control has an inverse: the input it must accept.

/** The exact fingerprint of the first scanned host key. */
const FIRST_FINGERPRINT = SCANNED_KEYS[0]?.fingerprint ?? ''

/** The exact fingerprint of the second scanned host key. */
const SECOND_FINGERPRINT = SCANNED_KEYS[1]?.fingerprint ?? ''

/**
 * Opens a server's page from the list, by the link that names it.
 * @param page The Playwright page under test.
 * @param name The server's name.
 * @returns Resolves once the server's heading is drawn.
 */
const openServer = async (page: Page, name: string): Promise<void> => {
  await openRemoteServersList(page)
  await page.getByRole('link', { name, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
}

/**
 * Builds a server whose discovery already ran, holding `count` accounts.
 * @param count How many accounts the stub holds for it.
 * @returns The server and its accounts, to seed the stub with.
 */
const discoveredServer = (count: number): { server: RemoteServerDetail; accounts: ReturnType<typeof discoveredAccount>[] } => {
  const accounts = Array.from({ length: count }, (_, index) => {
    const name = `acct${String(index + 1).padStart(2, '0')}`
    return discoveredAccount(name, { diskBytes: (index + 1) * 1024 * 1024, cronEntryCount: index === 2 ? 4 : 0 })
  })
  const server: RemoteServerDetail = {
    ...READY_SERVER,
    accountsFound: count,
    discoveredAt: '2026-11-03T12:00:00Z',
    warnings: [MAIL_WARNING],
  }
  return { server, accounts }
}

test.describe('the list', () => {
  test('is reached from the sidebar and shows each server with its address and state', async ({ page }) => {
    await openMigrationScreen(page, { servers: [FRESH_SERVER, READY_SERVER, CHANGED_SERVER] })

    await openRemoteServersList(page)

    const ready = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'legacy-web', exact: true }) })
    await expect(ready).toContainText('legacy.example.net:22 (root)')
    await expect(ready).toContainText('Ready')
    const fresh = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'old-shared', exact: true }) })
    await expect(fresh).toContainText('Host key to confirm')
    const changed = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'rebuilt-box', exact: true }) })
    await expect(changed).toContainText('Host key changed')
  })

  test('shows the empty state with the way to add the first server when the panel holds none', async ({ page }) => {
    await openMigrationScreen(page, { servers: [] })

    await openRemoteServersList(page)

    await expect(page.getByText('No remote servers yet')).toBeVisible()
    await expect(page.getByRole('table')).toHaveCount(0)
    await expect(page.getByRole('searchbox', { name: 'Search remote servers' })).toHaveCount(0)
  })

  test('sends a search to the panel, and a search matching nothing is the no-results state', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [FRESH_SERVER, READY_SERVER] })
    await openRemoteServersList(page)

    await page.getByRole('searchbox', { name: 'Search remote servers' }).fill('legacy')

    await expect(page).toHaveURL(/search=legacy/)
    await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'old-shared', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('search') === 'legacy'
      }),
    ).toBe(true)

    await page.getByRole('searchbox', { name: 'Search remote servers' }).fill('nowhere')
    await expect(page.getByText('No servers match')).toBeVisible()
    await expect(page.getByText('No remote servers yet')).toHaveCount(0)

    await page.getByRole('button', { name: 'Clear search and filters' }).click()
    await expect(page.getByRole('link', { name: 'old-shared', exact: true })).toBeVisible()
  })

  test('narrows by state through the filter sheet, shows a removable chip and asks the panel', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [FRESH_SERVER, READY_SERVER] })
    await openRemoteServersList(page)

    await page.getByTestId('filter-trigger').click()
    const sheet = page.getByRole('dialog', { name: 'Filters' })
    await sheet.getByRole('combobox', { name: 'State' }).click()
    await page.getByRole('option', { name: 'Ready' }).click()
    await sheet.getByRole('button', { name: 'Apply' }).click()

    await expect(page).toHaveURL(/state=ready/)
    await expect(page.getByTestId('active-filter-chips')).toContainText('State: Ready')
    await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toBeVisible()
    await expect(page.getByRole('link', { name: 'old-shared', exact: true })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('state') === 'ready'
      }),
    ).toBe(true)

    await page.getByRole('button', { name: 'Remove filter: State: Ready' }).click()
    await expect(page).not.toHaveURL(/state=/)
    await expect(page.getByRole('link', { name: 'old-shared', exact: true })).toBeVisible()
  })

  test('shows the panel\'s own words when it refuses the list, with no table beside them', async ({ page, guard }) => {
    // The refusal is the point of the test.
    guard.allow(503, /\/api\/v1\/remote-servers(\?.*)?$/)
    await openMigrationScreen(page, {
      servers: [READY_SERVER],
      refusals: { 'GET remote-servers': { detail: 'The migration module is switched off on this host.', status: 503 } },
    })

    await openRemoteServersList(page)

    await expect(page.getByText('The migration module is switched off on this host.')).toBeVisible()
    await expect(page.getByRole('table')).toHaveCount(0)
  })
})

test.describe('adding a server', () => {
  test('refuses an empty form without asking the panel, and states each problem', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [] })
    await openRemoteServersList(page)
    await page.getByRole('link', { name: 'Add a server' }).first().click()
    await expect(page.getByRole('heading', { level: 1, name: 'Add a remote server' })).toBeVisible()

    await page.getByRole('button', { name: 'Add the server' }).click()

    await expect(page.getByText('Enter a name.')).toBeVisible()
    await expect(page.getByText('Enter the host name or address.')).toBeVisible()
    await expect(page.getByText('Enter the login on the server.')).toBeVisible()
    expect(
      state.requests.some((request) => {
        return request.method === 'POST'
      }),
    ).toBe(false)
  })

  test('refuses a host with a scheme, a port out of range and an upper-case login', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [] })
    await openRemoteServersList(page)
    await page.getByRole('link', { name: 'Add a server' }).first().click()

    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('box')
    await page.getByRole('textbox', { name: 'Host name or address' }).fill('http://old.example.com')
    await page.getByRole('textbox', { name: 'SSH port' }).fill('70000')
    await page.getByRole('textbox', { name: 'Login on the server' }).fill('Root')
    await page.getByRole('button', { name: 'Add the server' }).click()

    await expect(page.getByText('Enter a host name or an IP address without spaces or a scheme.')).toBeVisible()
    await expect(page.getByText('The port is a whole number from 1 to 65535.')).toBeVisible()
    await expect(page.getByText(/A login starts with a lowercase letter/)).toBeVisible()
    expect(
      state.requests.some((request) => {
        return request.method === 'POST'
      }),
    ).toBe(false)
  })

  test('sends a valid form, then opens the server with its authorized_keys line and no private key', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [] })
    await openRemoteServersList(page)
    await page.getByRole('link', { name: 'Add a server' }).first().click()

    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('new-box')
    await page.getByRole('textbox', { name: 'Host name or address' }).fill('new.example.com')
    await page.getByRole('textbox', { name: 'Login on the server' }).fill('root')
    await page.getByRole('textbox', { name: 'Limit the key to these addresses' }).fill('203.0.113.10')
    await page.getByRole('button', { name: 'Add the server' }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'new-box' })).toBeVisible()
    await expect(page.getByText('The server new-box was added.')).toBeVisible()
    const post = state.requests.find((request) => {
      return request.method === 'POST'
    })
    expect(post?.body).toEqual({
      name: 'new-box',
      host: 'new.example.com',
      port: 22,
      sshUser: 'root',
      allowedFrom: '203.0.113.10',
    })
    await expect(page.getByTestId('authorized-keys-line')).toContainText('maran-migration')
    await expect(page.getByTestId('authorized-keys-line')).toContainText('no-port-forwarding')

    // The private key is never on the page. The positive control plants the marker and finds it, so
    // the hunt below is known to be able to see it.
    const hunt = async (): Promise<boolean> => {
      return (await page.content()).includes(PRIVATE_KEY_MARKER)
    }
    expect(await hunt()).toBe(false)
    await page.evaluate((marker) => {
      document.body.append(marker)
    }, PRIVATE_KEY_MARKER)
    expect(await hunt()).toBe(true)
  })

  test('shows the panel\'s refusal verbatim and stays on the form', async ({ page, guard }) => {
    guard.allow(502, /\/api\/v1\/remote-servers$/)
    await openMigrationScreen(page, {
      servers: [],
      refusals: { 'POST remote-servers': { detail: 'The server did not answer on port 22.', status: 502 } },
    })
    await openRemoteServersList(page)
    await page.getByRole('link', { name: 'Add a server' }).first().click()

    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('new-box')
    await page.getByRole('textbox', { name: 'Host name or address' }).fill('new.example.com')
    await page.getByRole('textbox', { name: 'Login on the server' }).fill('root')
    await page.getByRole('button', { name: 'Add the server' }).click()

    await expect(page.getByText('The server did not answer on port 22.')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Add a remote server' })).toBeVisible()
  })
})

test.describe('a server\'s page', () => {
  test('states the limit of the key and puts the line on the clipboard', async ({ page }) => {
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write'])
    await openMigrationScreen(page, { servers: [FRESH_SERVER] })
    await openServer(page, 'old-shared')

    await expect(page.getByText('This key cannot be limited to read-only.')).toBeVisible()
    await expect(page.getByText('The key works only from 203.0.113.10.')).toBeVisible()
    await page.getByRole('button', { name: 'Copy the line' }).click()

    await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible()
    const copiedText = await page.evaluate(() => {
      return navigator.clipboard.readText()
    })
    expect(copiedText).toBe(FRESH_SERVER.authorizedKeysLine)
  })

  test('says so when the server does not exist', async ({ page, guard }) => {
    guard.allow(404, /\/api\/v1\/remote-servers\/[^/?]+$/)
    await openMigrationScreen(page, {
      servers: [FRESH_SERVER],
      refusals: { 'GET remote-servers/:id': { detail: 'There is no such resource.', status: 404 } },
    })
    await openRemoteServersList(page)

    await page.getByRole('link', { name: 'old-shared', exact: true }).click()

    await expect(page.getByText('No such server')).toBeVisible()
  })

  test('changes a server through the form and forgets the host key when the target changes', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [READY_SERVER] })
    await openServer(page, 'legacy-web')

    await page.getByRole('link', { name: 'Edit' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Edit a remote server' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'Host name or address' })).toHaveValue('legacy.example.net')
    await expect(page.getByText('Changing the host, the port or the login forgets the confirmed host key.')).toBeVisible()
    await page.getByRole('textbox', { name: 'Host name or address' }).fill('moved.example.net')
    await page.getByRole('button', { name: 'Save changes' }).click()

    await expect(page.getByText('The server legacy-web was saved.')).toBeVisible()
    const put = state.requests.find((request) => {
      return request.method === 'PUT'
    })
    expect(put?.body).toMatchObject({ host: 'moved.example.net', port: 22, sshUser: 'root' })
    await expect(page.getByTestId('host-key-step')).toBeVisible()
  })
})

test.describe('confirming the host key', () => {
  test('keeps the confirm control off until a key is chosen and its fingerprint is retyped exactly', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [FRESH_SERVER] })
    await openServer(page, 'old-shared')
    const confirm = page.getByRole('button', { name: 'Confirm this host key' })
    const retype = page.getByRole('textbox', { name: 'Type or paste the fingerprint you chose' })

    // Nothing chosen, nothing typed.
    await expect(confirm).toBeDisabled()

    // A key chosen but not retyped.
    await page.getByRole('radio', { name: new RegExp(FIRST_FINGERPRINT) }).check({ force: true })
    await expect(confirm).toBeDisabled()

    // The other key's fingerprint typed: a mismatch, said so, still off.
    await retype.fill(SECOND_FINGERPRINT)
    await expect(page.getByText('This is not the chosen fingerprint.')).toBeVisible()
    await expect(confirm).toBeDisabled()

    // Inverse: the chosen fingerprint, typed exactly (a pasted trailing space is forgiven), turns it on.
    await retype.fill(`${FIRST_FINGERPRINT} `)
    await expect(confirm).toBeEnabled()
    expect(
      state.requests.some((request) => {
        return request.url.pathname.endsWith('/host-key')
      }),
    ).toBe(false)

    await confirm.click()

    await expect(page.getByText('The host key was confirmed.')).toBeVisible()
    await expect(page.getByTestId('pinned-fingerprint')).toHaveText(FIRST_FINGERPRINT)
    await expect(page.getByTestId('host-key-step')).toHaveCount(0)
    const post = state.requests.find((request) => {
      return request.url.pathname.endsWith('/host-key')
    })
    expect(post?.body).toEqual({ fingerprint: FIRST_FINGERPRINT })
  })

  test('shows the panel\'s refusal when it will not pin the key, and the key stays unconfirmed', async ({ page, guard }) => {
    guard.allow(409, /\/api\/v1\/remote-servers\/[^/?]+\/host-key$/)
    await openMigrationScreen(page, {
      servers: [FRESH_SERVER],
      refusals: { 'POST remote-servers/:id/host-key': { detail: 'The fingerprint is none of the scanned host keys.' } },
    })
    await openServer(page, 'old-shared')

    await page.getByRole('radio', { name: new RegExp(FIRST_FINGERPRINT) }).check({ force: true })
    await page.getByRole('textbox', { name: 'Type or paste the fingerprint you chose' }).fill(FIRST_FINGERPRINT)
    await page.getByRole('button', { name: 'Confirm this host key' }).click()

    await expect(page.getByText('The fingerprint is none of the scanned host keys.')).toBeVisible()
    await expect(page.getByTestId('host-key-step')).toBeVisible()
    await expect(page.getByTestId('pinned-fingerprint')).toHaveCount(0)
  })

  test('warns when the server presents another key than the one pinned, and offers the new key for review', async ({ page }) => {
    await openMigrationScreen(page, { servers: [CHANGED_SERVER] })
    await openServer(page, 'rebuilt-box')

    await expect(page.getByText('The server now presents a different host key than the one you confirmed.')).toBeVisible()
    await expect(page.getByRole('radio', { name: new RegExp(OTHER_FINGERPRINT) })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Confirm this host key' })).toBeDisabled()
    await expect(page.getByText('The server presented a different host key than the one you confirmed.')).toBeVisible()
  })

  test('puts no host key step in front of a server whose key is pinned and works', async ({ page }) => {
    await openMigrationScreen(page, { servers: [READY_SERVER] })
    await openServer(page, 'legacy-web')

    // Positive control: the confirmed key is on the page, so the missing step below is a finding.
    await expect(page.getByTestId('pinned-fingerprint')).toHaveText(FIRST_FINGERPRINT)
    await expect(page.getByTestId('host-key-step')).toHaveCount(0)
    await expect(page.getByText('The server now presents a different host key')).toHaveCount(0)
  })

  test('scans again on request and puts the step in front of a pinned key, which can be put away', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [PINNED_SERVER] })
    await openServer(page, 'pinned-only')

    await page.getByRole('button', { name: 'Review the host keys again' }).click()

    await expect(page.getByTestId('host-key-step')).toBeVisible()
    expect(
      state.requests.some((request) => {
        return request.url.pathname.endsWith('/host-keys/scan')
      }),
    ).toBe(true)
    await page.getByRole('button', { name: 'Keep the confirmed key' }).click()
    await expect(page.getByTestId('host-key-step')).toHaveCount(0)
  })
})

test.describe('testing the connection', () => {
  test('is off until the host key is confirmed, and says why', async ({ page }) => {
    await openMigrationScreen(page, { servers: [FRESH_SERVER] })
    await openServer(page, 'old-shared')

    await expect(page.getByRole('button', { name: 'Test the connection' })).toBeDisabled()
    await expect(page.getByText('Confirm the host key before the connection can be tested.')).toBeVisible()
  })

  test('reports a working connection with what the panel found on the server', async ({ page }) => {
    await openMigrationScreen(page, { servers: [PINNED_SERVER] })
    await openServer(page, 'pinned-only')

    await page.getByRole('button', { name: 'Test the connection' }).click()

    await expect(page.getByText('The connection works.')).toBeVisible()
    await expect(page.getByText('Debian GNU/Linux 12')).toBeVisible()
    await expect(page.getByText('50 GiB free of 100 GiB')).toBeVisible()
    await expect(page.getByText('Mail storage found. Mail is not migrated.')).toBeVisible()
  })

  test('names the typed failure when the server refuses the panel\'s key', async ({ page }) => {
    await openMigrationScreen(page, { servers: [PINNED_SERVER], testFailure: 'authRefused' })
    await openServer(page, 'pinned-only')

    await page.getByRole('button', { name: 'Test the connection' }).click()

    await expect(page.getByText('The server refused the panel\'s key.')).toBeVisible()
    await expect(page.getByText('The connection works.')).toHaveCount(0)
  })
})

test.describe('discovering accounts', () => {
  test('is the empty state until discovery ran, then lists what it found and tells what it could not read', async ({ page }) => {
    const { accounts } = discoveredServer(3)
    await openMigrationScreen(page, { servers: [READY_SERVER], accounts: { [READY_SERVER.id]: accounts }, warnings: [MAIL_WARNING] })
    await openServer(page, 'legacy-web')

    await page.getByRole('button', { name: 'Accounts', exact: true }).click()
    await expect(page.getByText('Nothing was discovered yet')).toBeVisible()
    await page.getByRole('button', { name: 'Discover accounts' }).click()

    await expect(page.getByText('Discovery finished. Accounts found: 3')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Discover again' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Details of the account acct01' })).toBeVisible()
    await expect(page.getByTestId('discovery-warnings')).toContainText('Mail is not migrated; the mailboxes of this account are left behind.')
  })

  test('refuses to discover a server whose host key is not confirmed', async ({ page }) => {
    await openMigrationScreen(page, { servers: [FRESH_SERVER] })
    await openServer(page, 'old-shared')

    await page.getByRole('button', { name: 'Accounts', exact: true }).click()

    await expect(page.getByRole('button', { name: 'Discover accounts' })).toBeDisabled()
    await expect(page.getByText('Confirm the host key before the panel can read the server.')).toBeVisible()
  })

  test('pages, searches, filters and sorts on the panel, never in the browser', async ({ page }) => {
    const { server, accounts } = discoveredServer(30)
    const state = await openMigrationScreen(page, { servers: [server], accounts: { [server.id]: accounts } })
    await openServer(page, 'legacy-web')
    await page.getByRole('button', { name: 'Accounts', exact: true }).click()

    // Paging: the first page holds 25 of 30, the next the last five, and the panel was asked for page 2.
    await expect(page.getByRole('button', { name: 'Details of the account acct01' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Details of the account acct26' })).toHaveCount(0)
    await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()
    await expect(page.getByRole('button', { name: 'Details of the account acct26' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Details of the account acct01' })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.pathname.endsWith('/accounts') && request.url.searchParams.get('page') === '2'
      }),
    ).toBe(true)

    // Search: by domain, and the panel was asked.
    await page.getByRole('searchbox', { name: 'Search accounts' }).fill('acct07.example')
    await expect(page.getByRole('button', { name: 'Details of the account acct07' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Details of the account acct08' })).toHaveCount(0)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('search') === 'acct07.example'
      }),
    ).toBe(true)

    // Filter: only accounts with cron entries (one of them), through the sheet.
    await page.getByRole('searchbox', { name: 'Search accounts' }).fill('')
    await page.getByTestId('filter-trigger').click()
    const sheet = page.getByRole('dialog', { name: 'Filters' })
    await sheet.getByRole('combobox', { name: 'Has' }).click()
    await page.getByRole('option', { name: 'Cron entries' }).click()
    await sheet.getByRole('button', { name: 'Apply' }).click()
    await expect(page.getByTestId('active-filter-chips')).toContainText('Has: Cron entries')
    await expect(page.getByRole('button', { name: 'Details of the account acct03' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Details of the account acct01' })).toHaveCount(0)

    // Sort: the disk column, descending after two presses, asked of the panel.
    await page.getByRole('button', { name: 'Remove filter: Has: Cron entries' }).click()
    const disk = page.getByRole('columnheader', { name: 'Disk' }).getByRole('button')
    await disk.click()
    await disk.click()
    await expect(page).toHaveURL(/sort=-disk/)
    expect(
      state.requests.some((request) => {
        return request.url.searchParams.get('sort') === '-disk'
      }),
    ).toBe(true)
    await expect(page.getByRole('button', { name: 'Details of the account acct30' })).toBeVisible()
  })

  test('opens an account with its sites and databases as read from the source', async ({ page }) => {
    const { server, accounts } = discoveredServer(2)
    await openMigrationScreen(page, { servers: [server], accounts: { [server.id]: accounts } })
    await openServer(page, 'legacy-web')
    await page.getByRole('button', { name: 'Accounts', exact: true }).click()

    await page.getByRole('button', { name: 'Details of the account acct01' }).click()

    const dialog = page.getByRole('dialog', { name: 'Account acct01' })
    await expect(dialog).toContainText('acct01.example.com')
    await expect(dialog).toContainText('www.acct01.example.com')
    await expect(dialog).toContainText('/home/acct01/public_html')
    await expect(dialog).toContainText('acct01_main')
    await page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
  })
})

test.describe('removing a server', () => {
  test('asks first, and abandoning the question sends nothing', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [READY_SERVER] })
    await openRemoteServersList(page)

    await page.getByRole('button', { name: 'Actions for the server legacy-web' }).click()
    await page.getByRole('menuitem', { name: 'Remove' }).click()
    const dialog = page.getByRole('dialog', { name: 'Remove the server legacy-web?' })
    await expect(dialog).toContainText('Nothing on the remote server changes')
    await dialog.getByRole('button', { name: 'Cancel' }).click()

    await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toBeVisible()
    expect(
      state.requests.some((request) => {
        return request.method === 'DELETE'
      }),
    ).toBe(false)
  })

  test('removes it, then reminds the administrator to take the panel\'s key off the source', async ({ page }) => {
    const state = await openMigrationScreen(page, { servers: [READY_SERVER, FRESH_SERVER] })
    await openRemoteServersList(page)

    await page.getByRole('button', { name: 'Actions for the server legacy-web' }).click()
    await page.getByRole('menuitem', { name: 'Remove' }).click()
    await page.getByRole('dialog', { name: 'Remove the server legacy-web?' }).getByRole('button', { name: 'Remove server' }).click()

    const reminder = page.getByRole('dialog', { name: 'Remove the panel\'s key from legacy-web' })
    await expect(reminder.getByTestId('removed-public-key')).toContainText(READY_SERVER.publicKey)
    expect(
      state.requests.some((request) => {
        return request.method === 'DELETE'
      }),
    ).toBe(true)

    await reminder.getByRole('button', { name: 'I removed it' }).click()

    await expect(page.getByText('The server legacy-web was removed.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'old-shared', exact: true })).toBeVisible()
  })

  test('removes from the server\'s own page and leaves for the list with the same reminder', async ({ page }) => {
    await openMigrationScreen(page, { servers: [READY_SERVER] })
    await openServer(page, 'legacy-web')

    await page.getByRole('button', { name: 'Remove', exact: true }).click()
    await page.getByRole('dialog', { name: 'Remove the server legacy-web?' }).getByRole('button', { name: 'Remove server' }).click()
    const reminder = page.getByRole('dialog', { name: 'Remove the panel\'s key from legacy-web' })
    await expect(reminder).toBeVisible()
    await reminder.getByRole('button', { name: 'I removed it' }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'Remote servers' })).toBeVisible()
    await expect(page.getByText('The server legacy-web was removed.')).toBeVisible()
  })

  test('shows the panel\'s refusal and keeps the server when it will not remove it', async ({ page, guard }) => {
    guard.allow(409, /\/api\/v1\/remote-servers\/[^/?]+$/)
    await openMigrationScreen(page, {
      servers: [READY_SERVER],
      refusals: { 'DELETE remote-servers/:id': { detail: 'The server is being used by a running migration.' } },
    })
    await openRemoteServersList(page)

    await page.getByRole('button', { name: 'Actions for the server legacy-web' }).click()
    await page.getByRole('menuitem', { name: 'Remove' }).click()
    await page.getByRole('dialog', { name: 'Remove the server legacy-web?' }).getByRole('button', { name: 'Remove server' }).click()

    await expect(page.getByText('The server is being used by a running migration.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'legacy-web', exact: true })).toBeVisible()
  })
})
