import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubEmptyTasks } from '../fixtures/stub-tasks-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import { offerRow, stubSubUsers, stubSubUsersForbidden, subUserRow } from '../fixtures/stub-sub-users-routes'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { Page } from '@playwright/test'

// Issues #315 and #328: an account's owner invites, resends, offers a place to an existing login,
// limits and removes team members from My account > Sub-users.
// Reached by clicks only: the sidebar's My account section, then its Sub-users link. The endpoints
// are the boundary (they refuse everyone but the owner); these specs prove what the screen does with
// their answers.

const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
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

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

/** Signs the owner in with the shell's own routes stubbed, then opens the team page from the sidebar. */
const openSubUsers = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubEmptyTasks(page)
  await stubLandingReads(page, 'customer')
  await stubMyAccount(page, OWN_ACCOUNT)
  await page.goto('/')
  await openSection(page, 'My account')
  await mainNavigation(page).getByRole('link', { name: 'Sub-users', exact: true }).click()
}

test('an owner opens the team page from My account and sees the people they added', async ({ page }) => {
  await stubSubUsers(page, [subUserRow('deploy', ['databases']), subUserRow('writer', [], 'invited')])
  await openSubUsers(page)

  await expect(page.getByRole('heading', { level: 1, name: 'Sub-users' })).toBeVisible()
  const deploy = page.getByRole('row', { name: /deploy/ })
  await expect(deploy).toContainText('deploy@example.com')
  await expect(deploy).toContainText('Active')
  await expect(deploy).toContainText('Databases')
  const writer = page.getByRole('row', { name: /writer/ })
  await expect(writer).toContainText('Invited')
  await expect(writer).toContainText('None')
})

test('an owner invites a person with chosen features and sees them invited', async ({ page }) => {
  const sent = await stubSubUsers(page, [])
  await openSubUsers(page)

  await page.getByLabel('Login name').fill('deploy')
  await page.getByLabel('E-mail address').fill('deploy@example.com')
  await page.getByRole('group', { name: 'Features this person may use' }).getByLabel('Databases').check({ force: true })
  await page.getByRole('group', { name: 'Features this person may use' }).getByLabel('Backups').check({ force: true })
  await page.getByRole('button', { name: 'Send invitation' }).click()

  await expect(page.getByText(/Invitation requested for deploy\./)).toBeVisible()
  expect(sent.invitations).toEqual([
    { username: 'deploy', email: 'deploy@example.com', features: ['databases', 'backups'] },
  ])
  const row = page.getByRole('row', { name: /deploy/ })
  await expect(row).toContainText('Invited')
  await expect(row).toContainText('Databases, Backups')
})

test('the invite form refuses a malformed login name and address without calling the panel', async ({ page }) => {
  const sent = await stubSubUsers(page, [])
  await openSubUsers(page)

  await page.getByLabel('Login name').fill('two words')
  await page.getByLabel('E-mail address').fill('nobody')
  await page.getByRole('button', { name: 'Send invitation' }).click()

  await expect(page.getByText('Use letters, digits, dots, underscores and dashes, up to 64 characters.')).toBeVisible()
  await expect(page.getByText('Enter a valid e-mail address.')).toBeVisible()
  expect(sent.invitations).toEqual([])
})

test('an owner changes the features of a person and the row follows', async ({ page }) => {
  const sent = await stubSubUsers(page, [subUserRow('deploy', ['databases'])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Change features' }).click()
  const dialog = page.getByRole('dialog', { name: 'Features of deploy' })
  await expect(dialog.getByLabel('Databases')).toBeChecked()
  await dialog.getByText('Databases', { exact: true }).click()
  await dialog.getByText('Cron jobs', { exact: true }).click()
  await dialog.getByRole('button', { name: 'Save features' }).click()

  await expect(page.getByText('Features of deploy saved.')).toBeVisible()
  expect(sent.changes).toEqual([{ features: ['cron'] }])
  await expect(page.getByRole('row', { name: /deploy/ })).toContainText('Cron jobs')
})

test('an owner removes a person only after confirming', async ({ page }) => {
  const sent = await stubSubUsers(page, [subUserRow('deploy', ['databases']), subUserRow('writer', [])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()
  expect(sent.removals).toEqual([])
  await expect(page.getByRole('row', { name: /deploy/ })).toBeVisible()

  await page.getByRole('button', { name: 'Actions for deploy' }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove sub-user' }).click()

  await expect(page.getByText('deploy removed.')).toBeVisible()
  expect(sent.removals).toHaveLength(1)
  await expect(page.getByRole('row', { name: /deploy/ })).toHaveCount(0)
  await expect(page.getByRole('row', { name: /writer/ })).toBeVisible()
})

test('a team member who opens the page reads the panel refusal and is offered no form', async ({ page, guard }) => {
  guard.allow(403, /\/api\/v1\/me\/sub-users(\/.*)?(\?.*)?$/)
  await stubSubUsersForbidden(page, 'Only the owner of the account can manage its sub-users.')
  await openSubUsers(page)

  await expect(page.getByText('Only the owner of the account can manage its sub-users.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send invitation' })).toHaveCount(0)
})

test('an owner with no team sees the empty state and the invite form', async ({ page }) => {
  await stubSubUsers(page, [])
  await openSubUsers(page)

  await expect(page.getByText('No sub-users yet')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send invitation' })).toBeVisible()
})

test('an owner resends the invitation of a person who has not accepted yet', async ({ page }) => {
  const sent = await stubSubUsers(page, [subUserRow('writer', [], 'invited')])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for writer' }).click()
  await page.getByRole('menuitem', { name: 'Resend invitation' }).click()

  await expect(page.getByText('A new invitation was sent to writer. The earlier link no longer works.')).toBeVisible()
  expect(sent.resends).toHaveLength(1)
  // Nothing was asked to confirm and the row did not change: the person is still invited.
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('row', { name: /writer/ })).toContainText('Invited')
})

test('a person who already signed in is not offered a resend', async ({ page }) => {
  await stubSubUsers(page, [subUserRow('deploy', ['databases'])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for deploy' }).click()

  await expect(page.getByRole('menuitem', { name: 'Change features' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Resend invitation' })).toHaveCount(0)
})

test('a refused resend shows the panel sentence verbatim', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/me\/sub-users\/[^/]+\/invitation$/)
  await stubSubUsers(page, [subUserRow('writer', [], 'invited')], { resendRefusal: 'That person has already accepted.' })
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for writer' }).click()
  await page.getByRole('menuitem', { name: 'Resend invitation' }).click()

  await expect(page.getByText('That person has already accepted.')).toBeVisible()
  await expect(page.getByText(/A new invitation was sent/)).toHaveCount(0)
})

test('an owner offers a place to an existing login and the person is shown as offered', async ({ page }) => {
  const sent = await stubSubUsers(page, [], { knownLogins: ['maria'] })
  await openSubUsers(page)

  await page.getByLabel('Existing login or e-mail').fill('maria')
  await page.getByRole('group', { name: 'Features to offer' }).getByLabel('Databases').check({ force: true })
  await page.getByRole('button', { name: 'Send offer' }).click()

  await expect(page.getByText('If that login exists, an offer was sent.')).toBeVisible()
  expect(sent.offers).toEqual([{ identifier: 'maria', features: ['databases'] }])
  const row = page.getByRole('row', { name: /maria/ })
  await expect(row).toContainText('Offer sent')
  await expect(row).toContainText('Databases')
})

test('the answer to an offer is the same for a login that does not exist', async ({ page }) => {
  const sent = await stubSubUsers(page, [], { knownLogins: ['maria'] })
  await openSubUsers(page)

  await page.getByLabel('Existing login or e-mail').fill('nobody-here')
  await page.getByRole('button', { name: 'Send offer' }).click()

  // The same sentence, the same request: nothing on screen confirms that the login is absent.
  await expect(page.getByText('If that login exists, an offer was sent.')).toBeVisible()
  expect(sent.offers).toEqual([{ identifier: 'nobody-here', features: [] }])
  await expect(page.getByRole('row', { name: /nobody-here/ })).toHaveCount(0)
})

test('the add-existing form refuses an empty identifier without calling the panel', async ({ page }) => {
  const sent = await stubSubUsers(page, [])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Send offer' }).click()

  await expect(page.getByText('Enter a login name or an e-mail address.')).toBeVisible()
  expect(sent.offers).toEqual([])
})

test('a refused offer shows the panel sentence verbatim and no success message', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/me\/sub-users\/existing$/)
  await stubSubUsers(page, [], { offerRefusal: 'You have reached the limit of sub-users for this account.' })
  await openSubUsers(page)

  await page.getByLabel('Existing login or e-mail').fill('maria')
  await page.getByRole('button', { name: 'Send offer' }).click()

  await expect(page.getByText('You have reached the limit of sub-users for this account.')).toBeVisible()
  await expect(page.getByText('If that login exists, an offer was sent.')).toHaveCount(0)
})

test('a pending offer can only be cancelled, and only after confirming', async ({ page }) => {
  const sent = await stubSubUsers(page, [offerRow('maria', ['databases'])])
  await openSubUsers(page)

  await page.getByRole('button', { name: 'Actions for maria' }).click()
  await expect(page.getByRole('menuitem', { name: 'Resend invitation' })).toHaveCount(0)
  await expect(page.getByRole('menuitem', { name: 'Change features' })).toHaveCount(0)
  await page.getByRole('menuitem', { name: 'Cancel offer' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(sent.removals).toEqual([])

  await page.getByRole('button', { name: 'Actions for maria' }).click()
  await page.getByRole('menuitem', { name: 'Cancel offer' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel offer' }).click()

  await expect(page.getByText('Offer to maria cancelled.')).toBeVisible()
  expect(sent.removals).toHaveLength(1)
  await expect(page.getByRole('row', { name: /maria/ })).toHaveCount(0)
})

/** What each language calls the things these specs click and read. */
const LOCALES = [
  {
    code: 'ru',
    navigation: 'Основная навигация',
    expand: 'Показать разделы «Мой аккаунт»',
    link: 'Субпользователи',
    actions: 'Действия для writer',
    resend: 'Отправить приглашение повторно',
    sent: 'Новое приглашение отправлено пользователю writer. Прежняя ссылка больше не действует.',
    identifier: 'Существующий логин или email',
    submit: 'Отправить предложение',
    offered: 'Если такой логин существует, предложение отправлено. Человек должен принять его, чтобы присоединиться.',
  },
  {
    code: 'hy',
    navigation: 'Հիմնական նավիգացիա',
    expand: 'Ցույց տալ «Իմ հաշիվը» բաժինները',
    link: 'Ենթաօգտատերեր',
    actions: 'writer-ի գործողությունները',
    resend: 'Կրկին ուղարկել հրավերը',
    sent: 'writer-ին նոր հրավեր ուղարկվեց։ Նախորդ հղումն այլևս չի գործում։',
    identifier: 'Առկա մուտքանուն կամ e-mail',
    submit: 'Ուղարկել առաջարկը',
    offered: 'Եթե այդպիսի մուտքանուն կա, առաջարկն ուղարկվել է։ Անձը պետք է ընդունի այն՝ միանալու համար։',
  },
] as const

for (const locale of LOCALES) {
  test(`resending and offering are localized in ${locale.code}`, async ({ page }) => {
    await setPersistedLocale(page, locale.code)
    await stubSubUsers(page, [subUserRow('writer', [], 'invited')])
    await stubSignedIn(page, CUSTOMER)
    await stubHealthy(page)
    await stubShellReads(page)
    await stubModules(page, FULL_CATALOGUE)
    await stubEmptyTasks(page)
    await stubLandingReads(page, 'customer')
    await stubMyAccount(page, OWN_ACCOUNT)
    await page.goto('/')
    const navigation = page.getByRole('navigation', { name: locale.navigation })
    await navigation.getByRole('button', { name: locale.expand }).click()
    await navigation.getByRole('link', { name: locale.link, exact: true }).click()

    await page.getByRole('button', { name: locale.actions }).click()
    await page.getByRole('menuitem', { name: locale.resend }).click()
    await expect(page.getByText(locale.sent)).toBeVisible()

    await page.getByLabel(locale.identifier).fill('nobody-here')
    await page.getByRole('button', { name: locale.submit }).click()
    await expect(page.getByText(locale.offered)).toBeVisible()
  })
}
