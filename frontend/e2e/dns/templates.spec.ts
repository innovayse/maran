import { expect, test } from '../fixtures/guarded-test'
import { CUSTOMER } from '../fixtures/open-dns-screen'
import { breadcrumbItems, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import {
  CREATED_TEMPLATE_ID,
  CUSTOM_TEMPLATE,
  WEB_AND_MAIL_TEMPLATE,
  WEB_TEMPLATE,
  openDnsTemplatesScreen,
  type DnsTemplatesStubState,
} from '../fixtures/stub-dns-templates-routes'
import type { Page } from '@playwright/test'

// The DNS zone templates screens (issue #452, administrators only): a server-paged list with a
// built-in and a default badge, a form that creates and changes a template and its records, a
// confirmed delete, and "make default". Whether a value may use a variable is the panel's
// decision: the stub refuses an unknown token the way the panel does, and the SPA shows that
// sentence as it was sent. The 390px contract in all three languages is the DNS sweep's
// (`no-horizontal-scroll.spec.ts`), which carries a row for each templates screen.

/** The three templates every list spec starts from. */
const SEED: Partial<DnsTemplatesStubState> = { templates: [WEB_TEMPLATE, WEB_AND_MAIL_TEMPLATE, CUSTOM_TEMPLATE] }

/**
 * Counts the requests the page sent with one method to a path ending.
 * @param state The stub's live state.
 * @param method The HTTP method.
 * @param ending The end of the path.
 * @returns How many matched.
 */
const sent = (state: DnsTemplatesStubState, method: string, ending: string): number => {
  return state.requests.filter((request) => { return request.method === method && request.url.pathname.endsWith(ending) }).length
}

/**
 * Fails when a locale key is drawn as text: vue-i18n renders the key itself for a missing one.
 * @param page The page under test.
 * @returns Resolves once the check is made.
 */
const expectNoRawKeys = async (page: Page): Promise<void> => {
  await expect(page.getByText(/\bdns\.(templates|zoneForm)\./)).toHaveCount(0)
}

/**
 * The list shows each template with its record count and the badges the panel states.
 * Planted failure: a row that drew the default badge on a non-default template (or none on the
 * default one) turns the first two assertions red. Inverse control: the custom template's row
 * carries neither badge, so a page that drew badges everywhere fails the last two.
 */
test('the templates list shows each template with its record count and its default and built-in badges', async ({ page }) => {
  await openDnsTemplatesScreen(page, SEED)

  await page.goto('/dns/templates')

  await expect(page.getByRole('heading', { level: 1, name: 'Zone templates' })).toBeVisible()
  const web = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'web', exact: true }) })
  await expect(web).toContainText('Default')
  await expect(web).toContainText('Built in')
  await expect(web.getByRole('cell', { name: '2', exact: true })).toBeVisible()
  const custom = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'parking', exact: true }) })
  await expect(custom).not.toContainText('Default')
  await expect(custom).not.toContainText('Built in')
})

/**
 * A search is sent to the panel, which answers the matching templates.
 * Planted failure: filtering in the browser instead leaves the request without `search=` and the
 * request assertion fails. Inverse control: before searching, all three templates are listed.
 */
test('a search is sent to the panel and only the matching templates are listed', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)

  await page.goto('/dns/templates')
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toBeVisible()
  await page.getByRole('searchbox', { name: 'Search templates' }).fill('park')

  await expect(page).toHaveURL(/search=park/)
  await expect(page.getByRole('link', { name: 'web', exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toBeVisible()
  expect(templates.requests.some((request) => { return request.url.searchParams.get('search') === 'park' })).toBe(true)
})

/**
 * With no template at all the list is the empty state with the way to create the first.
 * Planted failure: drawing the table with no rows fails the table-count assertion. Inverse
 * control: the no-results spec below opens the same page with templates present.
 */
test('no templates at all is the empty state', async ({ page }) => {
  await openDnsTemplatesScreen(page, { templates: [] })

  await page.goto('/dns/templates')

  await expect(page.getByText('No templates yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'New template' }).first()).toBeVisible()
})

/**
 * A search that matches nothing is the no-results state, not the empty state. Planted failure:
 * showing the empty state for a miss fails the second assertion. Inverse control: the same page
 * without the search lists the templates.
 */
test('a search that matches no template is the no-results state', async ({ page }) => {
  await openDnsTemplatesScreen(page, SEED)

  await page.goto('/dns/templates?search=nowhere')

  await expect(page.getByText('No templates match')).toBeVisible()
  await expect(page.getByText('No templates yet')).toHaveCount(0)
  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toBeVisible()
})

/**
 * Creating a template with a variable the panel does not list shows the panel's own refusal above
 * the form and keeps the form; correcting the value saves it and lands on the list with a notice.
 * Planted failure: a page that swallowed the refusal or navigated away would fail the first block;
 * a page that sent the corrected record with the old value fails the body assertion. Inverse
 * control: the second save is accepted by the same stub, so a stub refusing everything fails too.
 */
test('an unknown variable is refused in the panel\'s words and a corrected value is saved', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/dns-zone-templates$/)
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates/new')

  await page.getByRole('textbox', { name: 'Name' }).fill('landing')
  await page.getByRole('button', { name: 'Add record' }).click()
  let dialog = page.getByRole('dialog', { name: 'Add a record to the template' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('@')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('{bogus}')
  await dialog.getByRole('button', { name: 'Keep record' }).click()
  await expect(page.getByRole('row').filter({ hasText: '{bogus}' })).toContainText('Default')
  await page.getByRole('button', { name: 'Save template' }).click()

  await expect(page.getByText('The variable {bogus} is not one of the variables a template may use.')).toBeVisible()
  await expect(page).toHaveURL(/\/dns\/templates\/new$/)
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue('landing')

  await page.getByRole('button', { name: 'Actions for the A record @' }).click()
  await page.getByRole('menuitem', { name: 'Edit' }).click()
  dialog = page.getByRole('dialog', { name: 'Change a template record' })
  await dialog.getByRole('textbox', { name: 'Value' }).fill('{ipv4}')
  await dialog.getByRole('textbox', { name: 'TTL (seconds)' }).fill('300')
  await dialog.getByRole('button', { name: 'Keep record' }).click()
  await page.getByRole('button', { name: 'Save template' }).click()

  await expect(page).toHaveURL(/\/dns\/templates$/)
  await expect(page.getByText('The template landing was created.')).toBeVisible()
  const posts = templates.requests.filter((request) => { return request.method === 'POST' })
  expect(posts).toHaveLength(2)
  expect(posts[0]?.body).toMatchObject({ name: 'landing', records: [{ value: '{bogus}', ttl: null }] })
  expect(posts[1]?.body).toEqual({
    name: 'landing',
    description: '',
    records: [{ name: '@', type: 'a', ttl: 300, value: '{ipv4}', priority: 0, weight: 0, port: 0, flags: 0, tag: '' }],
  })
  expect(templates.templates.map((template) => { return template.id })).toContain(CREATED_TEMPLATE_ID)
})

/**
 * The variable list beside the form is whatever the panel sent. Planted failure: a page holding
 * its own list would show `{apex}`, which the seeded catalogue does not contain, and the absence
 * assertion fails. Inverse control: the seeded token and its description are visible.
 */
test('the variables shown are the ones the panel lists, not a list of the page\'s own', async ({ page }) => {
  await openDnsTemplatesScreen(page, {
    templates: [],
    variables: [{ token: '{only-here}', description: 'A variable this panel lists.' }],
  })

  await page.goto('/dns/templates/new')

  await expect(page.getByText('{only-here}')).toBeVisible()
  await expect(page.getByText('A variable this panel lists.')).toBeVisible()
  await expect(page.getByText('{apex}')).toHaveCount(0)
})

/**
 * A name is required before the panel is asked. Planted failure: submitting the empty name sends a
 * POST and the count assertion fails. Inverse control: the create spec above saves a named one.
 */
test('an empty name is refused without asking the panel', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates/new')

  await page.getByRole('button', { name: 'Save template' }).click()

  await expect(page.getByText('Enter a name for the template.')).toBeVisible()
  expect(sent(templates, 'POST', '/dns-zone-templates')).toBe(0)
})

/**
 * A record's TTL may be empty: it is sent as null, which the panel reads as the settings' default.
 * Planted failure: sending 0 or NaN for an empty TTL fails the body assertion. Inverse control:
 * the create spec above sends a typed 300.
 */
test('a record with no TTL is sent with a null TTL', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates/new')

  await page.getByRole('textbox', { name: 'Name' }).fill('plain')
  await page.getByRole('button', { name: 'Add record' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add a record to the template' })
  await dialog.getByRole('textbox', { name: 'Name' }).fill('www')
  await dialog.getByRole('textbox', { name: 'Value' }).fill('{ipv4}')
  await dialog.getByRole('button', { name: 'Keep record' }).click()
  await page.getByRole('button', { name: 'Save template' }).click()

  await expect(page).toHaveURL(/\/dns\/templates$/)
  const post = templates.requests.find((request) => { return request.method === 'POST' })
  expect(post?.body).toMatchObject({ records: [{ name: 'www', ttl: null }] })
})

/**
 * Changing a template loads it into the form, and saving replaces it whole. Planted failure: a
 * form that opened empty fails the prefill assertions; one that sent only the changed field fails
 * the body assertion (the record removed must be absent). Inverse control: the unchanged
 * description is still sent.
 */
test('changing a template loads it, and saving sends the name, the description and the remaining records', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates')

  await page.getByRole('link', { name: 'parking', exact: true }).click()

  await expect(page).toHaveURL(`/dns/templates/${CUSTOM_TEMPLATE.id}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Change template' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue('parking')
  await expect(page.getByRole('row').filter({ hasText: 'parked' })).toBeVisible()

  await page.getByRole('textbox', { name: 'Name' }).fill('parked')
  await page.getByRole('button', { name: 'Actions for the TXT record @' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  await expect(page.getByText('No records yet')).toBeVisible()
  await page.getByRole('button', { name: 'Save template' }).click()

  await expect(page).toHaveURL(/\/dns\/templates$/)
  await expect(page.getByText('The template parked was saved.')).toBeVisible()
  const put = templates.requests.find((request) => { return request.method === 'PUT' })
  expect(put?.url.pathname).toBe(`/api/v1/dns-zone-templates/${CUSTOM_TEMPLATE.id}`)
  expect(put?.body).toEqual({ name: 'parked', description: 'A parked domain.', records: [] })
})

/**
 * A template that does not exist is the not-found state, not an error alert. Planted failure:
 * treating the 404 as a failure shows the panel's sentence instead and the title assertion fails.
 * Inverse control: an existing template (the changing spec above) opens its form.
 */
test('opening a template that does not exist shows the not-found state', async ({ page, guard }) => {
  guard.allow(404, /\/api\/v1\/dns-zone-templates\/99999999-9999-9999-9999-999999999999$/)
  await openDnsTemplatesScreen(page, SEED)

  await page.goto('/dns/templates/99999999-9999-9999-9999-999999999999')

  await expect(page.getByText('No such template')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save template' })).toHaveCount(0)
})

/**
 * Deleting asks first, says what it does, sends nothing until confirmed, and reads the list again.
 * Planted failure: deleting on the menu click sends a DELETE before the dialog is confirmed and
 * the first count assertion fails. Inverse control: cancelling keeps the template, confirming
 * removes it.
 */
test('deleting a template asks first, cancelling keeps it and confirming removes it', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates')

  await page.getByRole('button', { name: 'Actions for the template parking' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  const dialog = page.getByRole('dialog', { name: 'Delete the template parking?' })
  await expect(dialog).toContainText('Zones already created from it keep their records')
  expect(sent(templates, 'DELETE', `/${CUSTOM_TEMPLATE.id}`)).toBe(0)

  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toBeVisible()
  expect(sent(templates, 'DELETE', `/${CUSTOM_TEMPLATE.id}`)).toBe(0)

  await page.getByRole('button', { name: 'Actions for the template parking' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete template' }).click()

  await expect(page.getByText('The template parking was deleted.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toHaveCount(0)
  expect(sent(templates, 'DELETE', `/${CUSTOM_TEMPLATE.id}`)).toBe(1)
})

/**
 * A built-in template cannot be deleted and the default one has no menu at all; the panel refuses
 * a delete of either regardless, which the next spec shows. Planted failure: offering Delete on a
 * built-in row fails the first assertion. Inverse control: the custom template's menu offers it.
 */
test('a built-in template offers no delete and the default one offers no menu', async ({ page }) => {
  await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates')

  await page.getByRole('button', { name: 'Actions for the template web and mail' }).click()
  await expect(page.getByRole('menuitem', { name: 'Make default' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Delete' })).toHaveCount(0)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'Actions for the template web', exact: true })).toHaveCount(0)

  await page.getByRole('button', { name: 'Actions for the template parking' }).click()
  await expect(page.getByRole('menuitem', { name: 'Delete' })).toBeVisible()
})

/**
 * A refused delete keeps the template and shows the panel's reason. Planted failure: dropping the
 * row before the answer fails the visibility assertion. Inverse control: an accepted delete
 * (above) does remove the row.
 */
test('a refused delete keeps the template and shows the panel\'s reason', async ({ page, guard }) => {
  guard.allow(409, /\/api\/v1\/dns-zone-templates\/[^/]+$/)
  await openDnsTemplatesScreen(page, {
    ...SEED,
    refusals: { 'DELETE templates/:id': 'A template that zones are created from cannot be deleted.' },
  })
  await page.goto('/dns/templates')

  await page.getByRole('button', { name: 'Actions for the template parking' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete template' }).click()

  await expect(page.getByText('A template that zones are created from cannot be deleted.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'parking', exact: true })).toBeVisible()
})

/**
 * Make default sends the request and the badge moves to the chosen template. Planted failure: a
 * page that only changed its own badges would send no POST, failing the count; one that did not
 * read the list again keeps the old badge, failing the last assertion. Inverse control: the
 * previous default is asserted to have lost the badge.
 */
test('making a template the default moves the badge to it', async ({ page }) => {
  const { templates } = await openDnsTemplatesScreen(page, SEED)
  await page.goto('/dns/templates')

  await page.getByRole('button', { name: 'Actions for the template web and mail' }).click()
  await page.getByRole('menuitem', { name: 'Make default' }).click()

  await expect(page.getByText('The template web and mail is now the default.')).toBeVisible()
  expect(sent(templates, 'POST', `/${WEB_AND_MAIL_TEMPLATE.id}/make-default`)).toBe(1)
  const mail = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'web and mail', exact: true }) })
  await expect(mail).toContainText('Default')
  const web = page.getByRole('row').filter({ has: page.getByRole('link', { name: 'web', exact: true }) })
  await expect(web).not.toContainText('Default')
})

/**
 * The screens are drawn in Russian and Armenian from their own locale files, with no key shown
 * as text. Planted failure: a key missing from a locale is drawn as the key and the raw-key
 * assertion fails; an English copy fails the heading assertion. Inverse control: the English
 * heading is absent in the other two locales.
 */
for (const [locale, heading, form] of [
  ['ru', 'Шаблоны зон', 'Новый шаблон'],
  ['hy', 'Գոտու ձևանմուշներ', 'Նոր ձևանմուշ'],
] as const) {
  test(`the templates screens are drawn in ${locale}`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await openDnsTemplatesScreen(page, SEED)

    await page.goto('/dns/templates')
    await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
    await expect(page.getByText('Zone templates')).toHaveCount(0)
    await expectNoRawKeys(page)

    await page.goto('/dns/templates/new')
    await expect(page.getByRole('heading', { level: 1, name: form })).toBeVisible()
    await expectNoRawKeys(page)
  })
}

/**
 * The administrator reaches the templates from the menu and the form from the list by clicks only.
 * Planted failure: Templates still drawn as a "coming soon" row has no link and the first click
 * fails. Inverse control: the customer's menu (next spec) offers no such link.
 */
test('the administrator reaches the templates and the new template form by clicks', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsTemplatesScreen(page, SEED)
  await stubLandingReads(page)
  await page.goto('/')

  await openSection(page, 'DNS')
  await mainNavigation(page).getByRole('link', { name: 'Templates', exact: true }).click()
  await expect(page).toHaveURL('/dns/templates')
  await expect(breadcrumbItems(page).last()).toHaveText('Templates')

  await page.getByRole('link', { name: 'New template' }).first().click()
  await expect(page).toHaveURL('/dns/templates/new')
  await expect(page.getByRole('heading', { level: 1, name: 'New template' })).toBeVisible()
})

/**
 * A customer is offered no templates entry and is turned away from the address. Planted failure:
 * dropping `adminOnly` from the route shows the screen and the heading count is not zero. Inverse
 * control: the same customer is offered the zone list (the DNS link).
 */
test('a customer is offered no templates and is turned away from the address', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsTemplatesScreen(page, SEED, {}, CUSTOMER)
  await stubLandingReads(page, 'customer')
  await page.goto('/')

  await expect(mainNavigation(page).getByRole('link', { name: 'DNS', exact: true })).toBeVisible()
  await expect(mainNavigation(page).getByRole('link', { name: 'Templates', exact: true })).toHaveCount(0)

  await page.goto('/dns/templates')
  await expect(page.getByRole('heading', { level: 1, name: 'Zone templates' })).toHaveCount(0)
})
