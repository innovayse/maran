import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { Account } from '../../src/types/account'

/** The account under test, active to begin with. */
const ACTIVE: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

/** The same account after the panel has suspended it. */
const SUSPENDED: Account = { ...ACTIVE, status: 'suspended' }

/**
 * Puts the accounts module in the catalogue and answers the detail read.
 * @param page The Playwright page whose network the routes are installed on.
 * @param account The account the panel reports.
 * @returns Resolves once the routes are installed.
 */
const stubDetail = async (page: Page, account: Account): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.route(`**/api/v1/accounts/${account.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(account) })
  })
}

test('the detail page shows the account and offers suspension while it is active', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)

  await expect(page.getByText('acme.example.com').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toHaveCount(0)
})

test('suspending asks in a dialog that names the account and what will happen', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()

  // The dialog's accessible name says WHICH account: a generic "Confirm" would read
  // the same to a screen-reader user whichever row they came from.
  await expect(page.getByRole('dialog', { name: 'Suspend account acme' })).toBeVisible()
  // Not "are you sure": the operator is being asked to weigh a consequence their
  // customer sees within seconds.
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Its sites stop serving and its user can no longer sign in.')
  // The consequence the panel added last and the only irreversible one: suspending ends the
  // transfer sessions the customer already has open, so a running upload is cut and what it had
  // written stays behind, shorter than it should be. `toContainText` is used because it is
  // case-sensitive — `getByText` and `hasText` match case-insensitive substrings, so a needle
  // shorter than this sentence would pass against copy that no longer says it.
  await expect(dialog).toContainText(
    'A file transfer running now is cut off, and the partial file it wrote stays in the account.',
  )
  // Vacuity guard on the axis that can go blind: the assertions above are substring matches, so
  // they would also hold on a dialog that had grown a second paragraph, and they say nothing about
  // the sentence being ALL the dialog says. This pins the whole consequence text, so copy that
  // silently gains or reorders a clause fails here rather than passing quietly.
  await expect(dialog.getByTestId('confirm-message')).toHaveText(
    'Suspend this account? Its sites stop serving and its user can no longer sign in. A file transfer running now is cut off, and the partial file it wrote stays in the account.',
  )
})

// This defect was invisible for exactly one reason: nobody opened the dialog in a browser, and the
// locale files are three separate sentences that no gate compares for MEANING — `maran structure`
// only proves the key exists in all three. So the cost is asserted in Russian too, as a value.
test('the Russian confirmation names the interrupted transfer as well', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Приостановить' }).click()

  const dialog = page.getByRole('dialog', { name: 'Приостановить аккаунт acme' })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByTestId('confirm-message')).toHaveText(
    'Приостановить аккаунт? Его сайты перестанут отвечать, а пользователь не сможет войти. Идущая передача файлов прервётся, а недописанный файл останется в аккаунте.',
  )
})

// And in Armenian, for the same reason and with one more: the Armenian clause was written with a
// construction that appeared nowhere else in this repository, and no gate here compares locales for
// MEANING — so the only thing that can hold the sentence still is its value, asserted. It now uses
// only vocabulary the other Armenian strings already use, and this is what fails if an unsourced
// term is put back.
test('the Armenian confirmation names the interrupted transfer as well', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Կասեցնել' }).click()

  const dialog = page.getByRole('dialog', { name: 'Կասեցնել acme հաշիվը' })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByTestId('confirm-message')).toHaveText(
    'Կասեցնե՞լ հաշիվը։ Կայքերը կդադարեն աշխատել, օգտատերը չի կարողանա մուտք գործել։ Ընթացիկ ֆայլերի փոխանցումը կընդհատվի, և արդեն գրվածը կմնա հաշվում։',
  )
})

// A modal whose default answer is "yes" is weaker than the sentence it replaced, so the
// focused control on open must not be the one that suspends the account.
test('the confirmation does not open with the confirming button focused', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Yes, do it' })).not.toBeFocused()
  // Focus is inside the dialog all the same — a trap around nothing is not a trap.
  await expect(dialog.locator(':focus')).toHaveCount(1)
})

// Escape is a dismissal, and a dismissal must not be an answer.
test('escaping the confirmation suspends nothing and gives the button its focus back', async ({
  page,
}) => {
  await stubDetail(page, ACTIVE)
  let suspendRequests = 0
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    suspendRequests += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SUSPENDED) })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(suspendRequests).toBe(0)
  // Back on the control that opened it, not at the top of the document.
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeFocused()
  // `exact: true`: the badge translates the status through a key built from it, and the English
  // label is the machine constant with a capital letter. A loose `getByText` is a
  // case-insensitive substring match, so it reads a badge that had regressed to printing `active` as a pass — the assertion
  // would say the state is still shown while saying nothing about it being shown in words.
  await expect(page.getByText('Active', { exact: true })).toBeVisible()
})

test('a confirmed suspension shows the new state without a reload', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SUSPENDED) })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  // Exact for the same reason as the sibling above: `suspended` is what the panel sends and
  // `Suspended` is what an operator must read, and only a case-sensitive whole-text match can tell
  // the two apart. The `Reactivate` button below follows the STATE, not its wording, so it cannot
  // stand in for this — without `exact` nothing in this test observed the label at all.
  await expect(page.getByText('Suspended', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible()
})

test('a refused action shows the panel message verbatim and changes nothing', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'The agent is unavailable.', detail: 'The agent is unavailable.', code: 'AgentUnavailable' }),
    })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByText('The agent is unavailable.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
})

// Deleting is confirmed by TYPING the account's name — e2e/accounts/delete.spec.ts proves that
// gesture and what the dialog says. What belongs here is that this page opens that dialog and not
// the yes/no one the two reversible actions use: both are modals now, so "a dialog appeared" no
// longer distinguishes them and the field is what does.
test('the delete button opens the typed confirmation, not the yes/no one', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByLabel('Type the account name to confirm')).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Yes, do it' })).toHaveCount(0)
})

test('the list opens an account by its name', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([ACTIVE]) })
  })

  await page.goto('/accounts')
  await page.getByRole('link', { name: 'acme' }).click()

  await expect(page).toHaveURL(`/accounts/${ACTIVE.id}`)
})
