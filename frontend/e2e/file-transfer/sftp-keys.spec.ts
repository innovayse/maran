import { expect, test, type Locator, type Page } from '@playwright/test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import {
  stubSftpUserKeys,
  stubSftpUsers,
  stubbedAddedKeyFingerprint,
  type SftpKeyRefusals,
  type SftpKeyStubReads,
} from '../fixtures/stub-sftp-routes'
import type { Account, MyAccount } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser, SftpUserKey } from '../../src/types/sftpUser'

// The public keys of an SFTP login (#135): a dialog opened from the row's menu that lists one server
// page of keys, adds a key, and removes one after a confirmation that says open sessions end.
// Every control is found by role and name, and the dialog is reached by clicks alone.

const LICENSED: PanelModule[] = [
  { name: 'sftp', displayName: 'File transfer', tier: 'included', state: 'enabled' },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', state: 'enabled' },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'web',
  fullName: 'alice_web',
  protocol: 'Sftp',
  createdAt: '2026-08-01T10:00:00Z',
}

/** The fingerprint of the laptop key, in the form `ssh-keygen -l` prints it. */
const LAPTOP_FINGERPRINT = 'SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8'

const LAPTOP: SftpUserKey = {
  id: 'bbbbbbbb-0000-0000-0000-000000000001',
  label: 'Work laptop',
  algorithm: 'ssh-ed25519',
  fingerprint: LAPTOP_FINGERPRINT,
  createdAt: '2026-08-10T10:00:00Z',
}

const BUILD_SERVER: SftpUserKey = {
  id: 'bbbbbbbb-0000-0000-0000-000000000002',
  label: 'Build server',
  algorithm: 'ecdsa-sha2-nistp256',
  fingerprint: 'SHA256:Zk0aGh2m3o4p5q6r7s8t9u0v1w2x3y4z5A6B7C8D9E0',
  createdAt: '2026-08-11T10:00:00Z',
}

/** A well-formed one-line key, with the comment the server drops. */
const NEW_KEY_LINE =
  'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl me@laptop'

/**
 * Builds numbered keys, `Key 01`, `Key 02` and so on.
 * @param count How many keys.
 * @returns The keys, in label order.
 */
const catalogue = (count: number): SftpUserKey[] => {
  return Array.from({ length: count }, (_, index): SftpUserKey => {
    const number = String(index + 1).padStart(2, '0')
    return {
      id: `cccccccc-0000-0000-0000-0000000000${number}`,
      label: `Key ${number}`,
      algorithm: 'ssh-ed25519',
      fingerprint: `SHA256:Fingerprint${number}Padding0123456789abcdefghijklmn`,
      createdAt: `2026-08-${number}T10:00:00Z`,
    }
  })
}

/**
 * Signs in as the administrator and serves one login with the given keys.
 * @param page The Playwright page.
 * @param keys The keys the panel reports for the login; mutated by an accepted add or remove.
 * @param refusals Refusals the keys endpoints answer with instead of accepting.
 * @returns Readers of what the page asked of the keys endpoints.
 */
const openWith = async (page: Page, keys: SftpUserKey[], refusals: SftpKeyRefusals = {}): Promise<SftpKeyStubReads> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubSftpUsers(page, [WEB])
  return await stubSftpUserKeys(page, keys, refusals)
}

/**
 * Opens the keys dialog of `alice_web` by clicks: the row's actions menu, then its Keys item.
 * @param page The Playwright page, already on a route the sidebar can leave.
 * @param labels The row trigger's name and the menu item's name, in the language under test.
 * @returns The dialog.
 */
const openKeysDialog = async (
  page: Page,
  labels = { trigger: 'Actions for alice_web', item: 'Keys', dialog: 'SSH keys for alice_web' },
): Promise<Locator> => {
  await page.goto('/file-transfer/sftp')
  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await row.getByRole('button', { name: labels.trigger }).click()
  // The menu is rendered into `body`, so it is queried from the page rather than the row.
  await page.getByRole('menuitem', { name: labels.item, exact: true }).click()
  const dialog = page.getByRole('dialog', { name: labels.dialog })
  await expect(dialog).toBeVisible()
  return dialog
}

/**
 * The rows of the key list, and only those: the pagination under it is a list too, so the dialog's
 * every `listitem` would count its page buttons as keys. The key list is the first list in the dialog.
 * @param dialog The keys dialog.
 * @returns The key rows.
 */
const keyRows = (dialog: Locator): Locator => {
  return dialog.getByRole('list').first().getByRole('listitem')
}

test('the keys dialog lists each key with its label, type, fingerprint and date', async ({ page }) => {
  const reads = await openWith(page, [LAPTOP, BUILD_SERVER])

  const dialog = await openKeysDialog(page)

  const laptop = keyRows(dialog).filter({ hasText: 'Work laptop' })
  await expect(laptop).toContainText('ssh-ed25519')
  await expect(laptop).toContainText(LAPTOP_FINGERPRINT)
  await expect(laptop).toContainText('Added')
  await expect(keyRows(dialog)).toHaveCount(2)
  await expect(dialog.getByText('No keys yet')).toHaveCount(0)
  // The first read asks for the smallest page size and no search.
  expect(reads.listRequests().at(0)?.searchParams.get('page')).toBe('1')
  expect(reads.listRequests().at(0)?.searchParams.get('pageSize')).toBe('10')
  expect(reads.listRequests().at(0)?.searchParams.get('search')).toBeNull()
})

test('a login with no key shows the empty state and the add form', async ({ page }) => {
  await openWith(page, [])

  const dialog = await openKeysDialog(page)

  await expect(dialog.getByText('No keys yet')).toBeVisible()
  await expect(keyRows(dialog)).toHaveCount(0)
  await expect(dialog.getByRole('button', { name: 'Add key' })).toBeVisible()
})

test('adding a key posts the label and the trimmed key line, lists it, and empties the form', async ({ page }) => {
  const reads = await openWith(page, [])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('textbox', { name: /^Label/ }).fill('  Home desktop ')
  await dialog.getByRole('textbox', { name: /^Public key/ }).fill(`${NEW_KEY_LINE}\n`)
  await dialog.getByRole('button', { name: 'Add key' }).click()

  await expect(keyRows(dialog).filter({ hasText: 'Home desktop' })).toContainText(stubbedAddedKeyFingerprint)
  expect(reads.added()).toEqual([{ label: 'Home desktop', publicKey: NEW_KEY_LINE }])
  await expect(dialog.getByRole('textbox', { name: /^Label/ })).toHaveValue('')
  await expect(dialog.getByRole('textbox', { name: /^Public key/ })).toHaveValue('')
  await expect(dialog.getByText('No keys yet')).toHaveCount(0)
})

test('the form refuses an empty label and a malformed key before sending anything', async ({ page }) => {
  const reads = await openWith(page, [])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('button', { name: 'Add key' }).click()
  await expect(dialog.getByText('Give the key a label.')).toBeVisible()
  await expect(dialog.getByText('Paste the public key.')).toBeVisible()

  await dialog.getByRole('textbox', { name: /^Label/ }).fill('Laptop')
  await dialog.getByRole('textbox', { name: /^Public key/ }).fill('ssh-dsa AAAAB3NzaC1kc3M=')
  await expect(dialog.getByText(/Unsupported key type/)).toBeVisible()

  await dialog.getByRole('textbox', { name: /^Public key/ }).fill('ssh-ed25519 AAAA\nssh-ed25519 BBBB')
  await expect(dialog.getByText(/A key is a single line/)).toBeVisible()

  expect(reads.added()).toEqual([])
})

// rules/vue.md: the backend owns the text of a refusal and the SPA renders it as received. The form
// keeps what was typed, because the person will correct it rather than retype it.
test('a refused key shows the backend sentence verbatim and keeps the typed values', async ({ page }) => {
  const detail = 'This login already holds the maximum of 32 keys.'
  const reads = await openWith(page, [], { add: detail })
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('textbox', { name: /^Label/ }).fill('Laptop')
  await dialog.getByRole('textbox', { name: /^Public key/ }).fill(NEW_KEY_LINE)
  await dialog.getByRole('button', { name: 'Add key' }).click()

  await expect(dialog.getByText(detail)).toBeVisible()
  expect(reads.added()).toHaveLength(1)
  await expect(dialog.getByRole('textbox', { name: /^Label/ })).toHaveValue('Laptop')
  await expect(keyRows(dialog)).toHaveCount(0)
})

test('removing a key asks first, says open sessions end, and a dismissal sends nothing', async ({ page }) => {
  const reads = await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('button', { name: 'Remove key Work laptop' }).click()

  const confirmation = page.getByRole('dialog', { name: 'Remove key Work laptop' })
  await expect(confirmation).toContainText('every open session of alice_web is ended')
  await confirmation.getByRole('button', { name: 'Cancel' }).click()

  await expect(confirmation).toHaveCount(0)
  expect(reads.removed()).toEqual([])
  await expect(keyRows(dialog)).toHaveCount(2)
})

test('a confirmed removal sends the DELETE for that key and the list refills without it', async ({ page }) => {
  const reads = await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('button', { name: 'Remove key Work laptop' }).click()
  await page
    .getByRole('dialog', { name: 'Remove key Work laptop' })
    .getByRole('button', { name: 'Remove key', exact: true })
    .click()

  await expect(page.getByRole('dialog', { name: 'Remove key Work laptop' })).toHaveCount(0)
  expect(reads.removed()).toEqual([`/api/v1/sftp-users/${WEB.id}/keys/${LAPTOP.id}`])
  await expect(keyRows(dialog)).toHaveCount(1)
  await expect(keyRows(dialog)).toContainText('Build server')
})

// The confirmation is a second modal over the keys dialog. Escape must answer the top one only, and
// focus must come back inside the keys dialog: if it fell to the page behind, the dialog's own
// Escape and Tab handling would never see the next key press.
test('Escape on the removal confirmation closes only the confirmation and focus returns to the key', async ({ page }) => {
  const reads = await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)
  const remove = dialog.getByRole('button', { name: 'Remove key Work laptop' })
  await remove.click()
  const confirmation = page.getByRole('dialog', { name: 'Remove key Work laptop' })
  await expect(confirmation).toBeVisible()

  await page.keyboard.press('Escape')

  await expect(confirmation).toHaveCount(0)
  await expect(dialog).toBeVisible()
  await expect(remove).toBeFocused()
  expect(reads.removed()).toEqual([])
  // Positive control: the keys dialog still answers Escape itself, so the check above saw a live dialog.
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('after a confirmed removal focus stays inside the keys dialog', async ({ page }) => {
  await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)
  await dialog.getByRole('button', { name: 'Remove key Work laptop' }).click()
  await page
    .getByRole('dialog', { name: 'Remove key Work laptop' })
    .getByRole('button', { name: 'Remove key', exact: true })
    .click()
  await expect(keyRows(dialog)).toHaveCount(1)

  await expect
    .poll(async () => {
      return await dialog.evaluate((element) => {
        return element.contains(element.ownerDocument.activeElement)
      })
    })
    .toBe(true)
})

test('a refused removal shows the backend sentence verbatim and keeps the key', async ({ page }) => {
  const detail = 'The open sessions of this login could not be ended. Nothing was changed.'
  await openWith(page, [LAPTOP], { remove: detail })
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('button', { name: 'Remove key Work laptop' }).click()
  await page
    .getByRole('dialog', { name: 'Remove key Work laptop' })
    .getByRole('button', { name: 'Remove key', exact: true })
    .click()

  await expect(dialog.getByText(detail)).toBeVisible()
  await expect(keyRows(dialog).filter({ hasText: 'Work laptop' })).toBeVisible()
})

test('twelve keys are paged by the server, and the second page is asked of the panel', async ({ page }) => {
  const reads = await openWith(page, catalogue(12))
  const dialog = await openKeysDialog(page)
  await expect(keyRows(dialog)).toHaveCount(10)

  await dialog.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: 'Next page' }).click()

  await expect(keyRows(dialog)).toHaveCount(2)
  await expect(keyRows(dialog).first()).toContainText('Key 11')
  expect(reads.listRequests().at(-1)?.searchParams.get('page')).toBe('2')
})

test('a label search is sent to the panel from the first page, and a miss is the no-results state', async ({ page }) => {
  const reads = await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('searchbox', { name: 'Search keys' }).fill('build')

  await expect(keyRows(dialog)).toHaveCount(1)
  await expect(keyRows(dialog)).toContainText('Build server')
  expect(reads.listRequests().at(-1)?.searchParams.get('search')).toBe('build')
  expect(reads.listRequests().at(-1)?.searchParams.get('page')).toBe('1')

  await dialog.getByRole('searchbox', { name: 'Search keys' }).fill('nothing')

  await expect(dialog.getByText('No keys match')).toBeVisible()
  await expect(dialog.getByText('No keys yet')).toHaveCount(0)
})

test('the dialog closes on its close button and the logins page is back', async ({ page }) => {
  await openWith(page, [LAPTOP])
  const dialog = await openKeysDialog(page)

  await dialog.getByRole('button', { name: 'Close' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('heading', { level: 1, name: 'SFTP logins' })).toBeVisible()
})

test('the keys dialog passes the axe scan', async ({ page }) => {
  await openWith(page, [LAPTOP, BUILD_SERVER])
  const dialog = await openKeysDialog(page)
  await expect(keyRows(dialog)).toHaveCount(2)

  await expectNoSeriousViolations(page)
})

test('the Russian dialog is drawn in Russian from the row menu to the confirmation', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openWith(page, [LAPTOP])

  const dialog = await openKeysDialog(page, {
    trigger: 'Действия для alice_web',
    item: 'Ключи',
    dialog: 'SSH-ключи для alice_web',
  })

  await expect(dialog.getByRole('searchbox', { name: 'Поиск ключей' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Добавить ключ' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Удалить ключ Work laptop' }).click()
  await expect(page.getByRole('dialog', { name: 'Удалить ключ Work laptop' })).toContainText(
    'все открытые сеансы alice_web будут завершены',
  )
})

test('the Armenian dialog is drawn in Armenian from the row menu to the confirmation', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await openWith(page, [LAPTOP])

  const dialog = await openKeysDialog(page, {
    trigger: 'Գործողություններ alice_web-ի համար',
    item: 'Բանալիներ',
    dialog: 'SSH բանալիներ՝ alice_web',
  })

  await expect(dialog.getByRole('searchbox', { name: 'Որոնել բանալիներ' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Ավելացնել բանալի' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Հեռացնել Work laptop բանալին' }).click()
  await expect(page.getByRole('dialog', { name: 'Հեռացնել Work laptop բանալին' })).toContainText(
    'alice_web լոգինի բոլոր բաց սեանսները կավարտվեն',
  )
})

// The fingerprint is one unbroken run and the Armenian chrome is long: neither may push the page or
// the dialog sideways at the narrowest width the panel supports.
for (const locale of ['en', 'hy'] as const) {
  test(`at 390px the ${locale} keys dialog does not scroll sideways`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 780 })
    await setPersistedLocale(page, locale)
    await openWith(page, [{ ...LAPTOP, label: 'A very long label for a key that lives on a laptop in a drawer' }])

    const dialog = await openKeysDialog(
      page,
      locale === 'en'
        ? { trigger: 'Actions for alice_web', item: 'Keys', dialog: 'SSH keys for alice_web' }
        : { trigger: 'Գործողություններ alice_web-ի համար', item: 'Բանալիներ', dialog: 'SSH բանալիներ՝ alice_web' },
    )
    await expect(keyRows(dialog)).toHaveCount(1)

    const fit = await dialog.evaluate((element) => {
      const root = element.ownerDocument.documentElement
      const box = element.getBoundingClientRect()
      return {
        pageOverflow: root.scrollWidth - root.clientWidth,
        dialogOverflow: element.scrollWidth - element.clientWidth,
        right: box.right,
        viewport: root.clientWidth,
      }
    })
    expect(fit.pageOverflow).toBe(0)
    expect(fit.dialogOverflow).toBeLessThanOrEqual(0)
    expect(fit.right).toBeLessThanOrEqual(fit.viewport)
  })
}
