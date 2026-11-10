import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { mainNavigation, openSection } from '../fixtures/open-full-shell'
import { EXAMPLE_ZONE } from '../fixtures/open-dns-screen'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import {
  DNSKEY_PUBLIC_KEY,
  DS_DIGEST,
  OFF_DNSSEC,
  SIGNED_DNSSEC,
  UNAVAILABLE_DNSSEC,
  WITHDRAWING_DNSSEC,
  WITHDRAWING_EARLY_DNSSEC,
  openDnsDnssecScreen,
  type DnsDnssecStubState,
} from '../fixtures/stub-dns-dnssec-routes'
import type { ZoneDnssec } from '../../src/types/dnsDnssec'
import type { Page } from '@playwright/test'

// The DNSSEC pane of a zone (issue #452): the DS record and DNSKEY a signed zone publishes, each
// copyable; the pane hidden on a host that cannot sign; and the three transitions, each asked
// first, the two that remove the signing carrying the outage warning (remove the DS record at the
// registrar FIRST). The 48-hour wait is the panel's rule: the finish control reflects the earliest
// instant the panel states and the panel's refusal is shown as it was sent. Desktop specs reach the
// pane by clicks only (menu, list, row, tab); the language and 390px specs open the zones list
// directly, as the DNS sweep does, because the menu's names differ per language.

/** The sentence the stub's panel refuses an early finish with; the SPA must show it as sent. */
const TOO_EARLY = 'The signing was withdrawn too recently. It can be finished after 3 Jan 2026.'

/**
 * Reaches the DNSSEC pane of the example zone by clicks only: the DNS menu, the zones list, the
 * zone's row and the pane's tab.
 * @param page The Playwright page under test.
 * @returns Resolves once the pane's tab is pressed.
 */
const openPaneByClicks = async (page: Page): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubLandingReads(page)
  await page.goto('/')
  await openSection(page, 'DNS')
  await mainNavigation(page).getByRole('link', { name: 'Zones', exact: true }).click()
  await page.getByRole('link', { name: 'example.com', exact: true }).click()
  await page.getByRole('button', { name: 'DNSSEC', exact: true }).click()
}

/**
 * Opens the example zone with a stubbed signing and reaches the pane by clicks.
 * @param page The Playwright page under test.
 * @param dnssec The signing the panel holds.
 * @param extra Anything else to seed the DNSSEC stub with, such as refusals.
 * @returns The DNSSEC stub's live state.
 */
const openPane = async (page: Page, dnssec: ZoneDnssec, extra: Partial<DnsDnssecStubState> = {}): Promise<DnsDnssecStubState> => {
  const opened = await openDnsDnssecScreen(page, { dnssec, ...extra }, { zones: [EXAMPLE_ZONE] })
  await openPaneByClicks(page)
  return opened.dnssec
}

/**
 * Counts the requests the page sent with one method to a path ending.
 * @param state The stub's live state.
 * @param method The HTTP method.
 * @param ending The end of the path.
 * @returns How many matched.
 */
const sent = (state: DnsDnssecStubState, method: string, ending: string): number => {
  return state.requests.filter((request) => { return request.method === method && request.url.pathname.endsWith(ending) }).length
}

/**
 * A signed zone shows its DS record field by field and the DNSKEY, each with a copy control that
 * puts exactly that field on the clipboard, and says in plain text that keys do not roll over.
 * Planted failure: a copy control that copied another field (the key tag for the digest) turns the
 * clipboard assertion red; a pane that dropped the no-rollover sentence turns the last assertion
 * red. Inverse control: the key tag's own control copies the key tag, so a control that copied the
 * digest everywhere fails the second clipboard assertion.
 */
test('a signed zone shows its DS record and DNSKEY, each copyable, and says there is no automatic key rollover', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openPane(page, SIGNED_DNSSEC)

  await expect(page.getByTestId('ds-keyTag')).toHaveText('20326')
  await expect(page.getByTestId('ds-algorithm')).toHaveText('13')
  await expect(page.getByTestId('ds-digestType')).toHaveText('2')
  await expect(page.getByTestId('ds-digest')).toHaveText(DS_DIGEST)
  await expect(page.getByTestId('dnskey-publicKey')).toHaveText(DNSKEY_PUBLIC_KEY)
  await expect(page.getByText('Confirmed by the name server').first()).toBeVisible()

  await page.getByRole('button', { name: 'Copy Digest', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Digest copied', exact: true })).toBeVisible()
  expect(await page.evaluate(() => { return navigator.clipboard.readText() })).toBe(DS_DIGEST)

  await page.getByRole('button', { name: 'Copy Key tag', exact: true }).click()
  expect(await page.evaluate(() => { return navigator.clipboard.readText() })).toBe('20326')

  await expect(page.getByTestId('dnssec-no-rollover')).toContainText('There is no automatic key rollover')
})

/**
 * The pane is not offered when the host cannot sign and the zone is not signed, and is offered
 * when the host can, and when the zone is signed even though the host now cannot (its DS record
 * must stay reachable).
 * Planted failure: showing the tab regardless turns the first assertion red; hiding it by
 * `available` alone turns the third red. Inverse control: the second and third cases show it.
 */
test('the DNSSEC tab is hidden when the host cannot sign, and kept for a zone that is already signed', async ({ page }) => {
  await openDnsDnssecScreen(page, { dnssec: UNAVAILABLE_DNSSEC }, { zones: [EXAMPLE_ZONE] })
  await page.goto('/dns/zones')
  await page.getByRole('link', { name: 'example.com', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Records', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'DNSSEC', exact: true })).toHaveCount(0)
})

test('the DNSSEC tab is offered when the host can sign', async ({ page }) => {
  await openPane(page, OFF_DNSSEC)

  await expect(page.getByRole('button', { name: 'Enable DNSSEC' })).toBeEnabled()
})

test('a signed zone keeps its DNSSEC tab on a host that can no longer sign', async ({ page }) => {
  await openPane(page, { ...SIGNED_DNSSEC, available: false })

  await expect(page.getByTestId('ds-digest')).toHaveText(DS_DIGEST)
})

/**
 * Enabling asks first and sends nothing until confirmed; confirming signs the zone and shows the DS.
 * Planted failure: a button that enabled without asking leaves a POST in the request log before
 * the confirmation and fails the first count. Inverse control: cancelling sends nothing, and
 * confirming sends exactly one request.
 */
test('enabling DNSSEC asks first, and confirming shows the DS record to publish', async ({ page }) => {
  const stub = await openPane(page, OFF_DNSSEC)

  await page.getByRole('button', { name: 'Enable DNSSEC' }).click()
  const dialog = page.getByRole('dialog', { name: 'Enable DNSSEC for example.com?' })
  await expect(dialog).toContainText('Then publish the DS record at your registrar')
  expect(sent(stub, 'POST', '/dnssec/enable')).toBe(0)

  await dialog.getByRole('button', { name: 'Cancel' }).click()
  // The dialog leaves with a transition; until it is gone its confirm button shares the opener's name.
  await expect(dialog).toBeHidden()
  expect(sent(stub, 'POST', '/dnssec/enable')).toBe(0)

  await page.getByRole('button', { name: 'Enable DNSSEC' }).click()
  await page.getByRole('dialog', { name: 'Enable DNSSEC for example.com?' }).getByRole('button', { name: 'Enable DNSSEC' }).click()

  await expect(page.getByTestId('ds-digest')).toHaveText(DS_DIGEST)
  await expect(page.getByText('Signed', { exact: true })).toBeVisible()
  expect(sent(stub, 'POST', '/dnssec/enable')).toBe(1)
})

/**
 * Withdrawing asks first and the question carries the mandatory outage warning: remove the DS
 * record at the registrar first, never last.
 * Planted failure: a confirmation without the warning sentence fails the text assertion; one that
 * sent the request before confirming fails the first count. Inverse control: cancelling sends
 * nothing and leaves the zone signed.
 */
test('withdrawing DNSSEC asks first and warns to remove the DS record at the registrar first', async ({ page }) => {
  const stub = await openPane(page, SIGNED_DNSSEC)

  await page.getByRole('button', { name: 'Withdraw DNSSEC' }).click()
  const dialog = page.getByRole('dialog', { name: 'Withdraw DNSSEC from example.com?' })
  await expect(dialog.getByTestId('confirm-message')).toContainText(
    'Remove the DS record at your registrar first, never last.',
  )
  expect(sent(stub, 'POST', '/dnssec/withdraw')).toBe(0)

  await dialog.getByRole('button', { name: 'Cancel' }).click()
  // The dialog leaves with a transition; until it is gone its confirm button shares the opener's name.
  await expect(dialog).toBeHidden()
  await expect(page.getByText('Signed', { exact: true })).toBeVisible()
  expect(sent(stub, 'POST', '/dnssec/withdraw')).toBe(0)

  await page.getByRole('button', { name: 'Withdraw DNSSEC' }).click()
  await page.getByRole('dialog', { name: 'Withdraw DNSSEC from example.com?' }).getByRole('button', { name: 'Withdraw DNSSEC' }).click()

  await expect(page.getByText('Withdrawing', { exact: true })).toBeVisible()
  expect(sent(stub, 'POST', '/dnssec/withdraw')).toBe(1)
})

/**
 * Finishing needs the explicit statement that the DS record was removed, and is disabled until the
 * earliest instant the panel states, which is shown.
 * Planted failure: a finish control that ignored the checkbox is enabled in the first assertion
 * and fails; one that ignored the earliest instant is enabled in the third and fails. Inverse
 * control: with the wait over and the box ticked it is enabled, asks, and sends `dsRecordRemoved`
 * true.
 */
test('finishing the withdrawal needs the statement that the DS record is gone, and waits for the earliest time', async ({ page }) => {
  const stub = await openPane(page, WITHDRAWING_DNSSEC)

  const finish = page.getByRole('button', { name: 'Finish withdrawal' })
  await expect(finish).toBeDisabled()
  await page.getByRole('checkbox', { name: 'I removed the DS record at the registrar' }).check({ force: true })
  await expect(finish).toBeEnabled()

  await finish.click()
  const dialog = page.getByRole('dialog', { name: 'Finish withdrawing DNSSEC from example.com?' })
  await expect(dialog.getByTestId('confirm-message')).toContainText('Confirm only if you removed the DS record at the registrar first.')
  expect(sent(stub, 'POST', '/dnssec/finish-withdrawal')).toBe(0)
  await dialog.getByRole('button', { name: 'Finish withdrawal' }).click()

  await expect(page.getByText('Not signed', { exact: true })).toBeVisible()
  const request = stub.requests.find((candidate) => { return candidate.url.pathname.endsWith('/dnssec/finish-withdrawal') })
  expect(request?.body).toEqual({ dsRecordRemoved: true })
})

test('before the earliest time the finish control is disabled and the time is shown', async ({ page }) => {
  await openPane(page, WITHDRAWING_EARLY_DNSSEC)

  await page.getByRole('checkbox', { name: 'I removed the DS record at the registrar' }).check({ force: true })

  await expect(page.getByTestId('dnssec-earliest')).toContainText('The withdrawal can be finished from')
  await expect(page.getByRole('button', { name: 'Finish withdrawal' })).toBeDisabled()
})

/**
 * When the control is enabled but the panel still refuses (its clock, its rule), the refusal is
 * shown exactly as the backend sent it and the zone stays in its withdrawal.
 * Planted failure: a SPA that swallowed the refusal, or replaced it with its own text, fails the
 * text assertion; one that moved to Not signed anyway fails the second. Inverse control: the
 * acceptance case above leaves the same screen on Not signed.
 */
test('an early finish the panel refuses shows the panel\'s sentence and keeps the withdrawal', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/dns-zones\/[^/]+\/dnssec\/finish-withdrawal$/)
  await openPane(page, WITHDRAWING_DNSSEC, { refusals: { 'POST dnssec/finish-withdrawal': TOO_EARLY } })

  await page.getByRole('checkbox', { name: 'I removed the DS record at the registrar' }).check({ force: true })
  await page.getByRole('button', { name: 'Finish withdrawal' }).click()
  await page.getByRole('dialog', { name: 'Finish withdrawing DNSSEC from example.com?' }).getByRole('button', { name: 'Finish withdrawal' }).click()

  await expect(page.getByText(TOO_EARLY)).toBeVisible()
  await expect(page.getByText('Withdrawing', { exact: true })).toBeVisible()
  await expect(page.getByText('Not signed', { exact: true })).toHaveCount(0)
})

/**
 * Deleting a signed zone adds the DS warning to the confirmation; deleting an unsigned zone does not.
 * Planted failure: a confirmation without the DS sentence for a signed zone fails the first
 * assertion. Inverse control: the unsigned zone's confirmation must NOT carry it, so a dialog that
 * warned always fails the second test.
 */
test('deleting a signed zone warns about the DS record at the registrar', async ({ page }) => {
  await openPane(page, SIGNED_DNSSEC)

  await page.getByRole('button', { name: 'Delete', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete the zone example.com?' })
  await expect(dialog.getByTestId('confirm-message')).toContainText('remove the DS record at your registrar first')
})

test('deleting an unsigned zone carries no DS warning', async ({ page }) => {
  await openPane(page, OFF_DNSSEC)

  await page.getByRole('button', { name: 'Delete', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete the zone example.com?' })
  await expect(dialog.getByTestId('confirm-message')).toContainText('Every record in the zone is deleted')
  await expect(dialog.getByTestId('confirm-message')).not.toContainText('DS record')
})

test('deleting a signed zone from the list warns about the DS record too', async ({ page }) => {
  await openDnsDnssecScreen(page, { dnssec: SIGNED_DNSSEC }, { zones: [EXAMPLE_ZONE] })
  await page.goto('/dns/zones')

  await page.getByRole('button', { name: 'Actions for the zone example.com' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()

  await expect(page.getByRole('dialog', { name: 'Delete the zone example.com?' }).getByTestId('confirm-message')).toContainText(
    'remove the DS record at your registrar first',
  )
})

/** The interface languages the panel ships, with the state word each draws for a signed zone. */
const LOCALES = [
  { code: 'en', signed: 'Signed' },
  { code: 'ru', signed: 'Подписана' },
  { code: 'hy', signed: 'Ստորագրված է' },
] as const

for (const locale of LOCALES) {
  /**
   * The pane is drawn in each language with no raw key, and does not scroll sideways at 390px with
   * a digest and a public key that cannot break by themselves.
   * Planted failure: a key missing from one locale draws as `dns.dnssec.…` and fails the raw-key
   * assertion; an unbreakable digest in a nowrap box fails the overflow measure. Inverse control:
   * the English run, whose strings the other specs assert, passes the same measure.
   */
  test(`the DNSSEC pane is translated and does not scroll sideways at 390px in ${locale.code}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale.code)
    await openDnsDnssecScreen(page, { dnssec: WITHDRAWING_EARLY_DNSSEC }, { zones: [EXAMPLE_ZONE] })

    await page.goto('/dns/zones')
    await page.getByRole('link', { name: 'example.com', exact: true }).click()
    // The pane is the third tab in every language, where the labels are not the same.
    await page.getByRole('main').locator('[role="group"] > button').nth(2).click()

    await expect(page.getByTestId('ds-digest')).toHaveText(DS_DIGEST)
    await expect(page.getByText(/\bdns\.(dnssec|detail|zones)\./)).toHaveCount(0)
    await expectNoHorizontalOverflow(page)
  })

  test(`a signed zone is named as signed in ${locale.code}`, async ({ page }) => {
    await setPersistedLocale(page, locale.code)
    await openDnsDnssecScreen(page, { dnssec: SIGNED_DNSSEC }, { zones: [EXAMPLE_ZONE] })

    await page.goto('/dns/zones')
    await page.getByRole('link', { name: 'example.com', exact: true }).click()
    await page.getByRole('main').locator('[role="group"] > button').nth(2).click()

    await expect(page.getByText(locale.signed, { exact: true })).toBeVisible()
  })
}
