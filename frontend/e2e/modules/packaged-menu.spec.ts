import { expect, test } from '../fixtures/guarded-test'
import { GOOD_SCRIPT, NAVIGATE_SCRIPT } from '../fixtures/module-frame-scripts'
import { openPackagedShell, packagedMenuLink } from '../fixtures/open-packaged-module'
import {
  COMPILED_IN_MODULE,
  PACKAGED_FRAME_URL,
  PACKAGED_ID,
  PACKAGED_TEXT,
  packagedModule,
  type SpecLocale,
} from '../fixtures/stub-packaged-module'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import type { Page } from '@playwright/test'

// Issue #185: an installed module appears in the menu and opens in an isolated frame WITHOUT the SPA
// being rebuilt. The catalogue is the only thing that tells the shell the module exists; its menu
// labels and screen titles are the backend's own text, in the language the request carried.

/** The interface languages the panel ships. */
const LOCALES: readonly SpecLocale[] = ['en', 'ru', 'hy']

/** The group heading the installed modules are drawn under, per language (`app.nav.groups.modules`). */
const GROUP_LABEL: Readonly<Record<SpecLocale, string>> = { en: 'Modules', ru: 'Модули', hy: 'Մոդուլներ' }

/**
 * Answers the sample module's own endpoint the way its backend would.
 * @param page The page under test.
 * @returns Resolves once installed.
 */
const stubHelloApi = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/hello/greeting', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ greeting: 'Hello' }) })
  })
}

for (const locale of LOCALES) {
  test(`an installed module appears in the menu and renders data through the bridge in ${locale}`, async ({ page }) => {
    const text = PACKAGED_TEXT[locale]
      await openPackagedShell(page, { script: GOOD_SCRIPT, locale, stubs: stubHelloApi })

    // In the menu under its own group, named by the backend's text and led by the neutral glyph.
    const link = packagedMenuLink(page)
    await expect(page.locator('#nav-group-modules')).toBeVisible()
    await expect(page.locator('nav').getByText(GROUP_LABEL[locale], { exact: true })).toBeVisible()
    await expect(link).toContainText(text.menu)
    await expect(link).not.toHaveAttribute('aria-disabled', 'true')

    await link.click()
    await expect(page).toHaveURL(`/m/${PACKAGED_ID}/hello`)

    // One h1, the screen's name from the catalogue; the module's own content cannot add another.
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1)
    await expect(page.getByRole('heading', { level: 1, name: text.title })).toBeVisible()
    await expect(page).toHaveTitle(`${text.title} · Maran`)

    // The frame is named and tells the shell what it knows: language, theme, role name, route.
    const frame = page.frameLocator(`iframe[src^="${PACKAGED_FRAME_URL}"]`)
    await expect(
      frame.getByText(new RegExp(`init locale:${locale} theme:(dark|light) role:admin route:/hello`)),
    ).toBeVisible()
    // The data went out through the shell's session and came back over the port.
    await expect(frame.getByText('result greeting 200 {"greeting":"Hello"}')).toBeVisible()
    // The module's toast is shown as text by the shell.
    await expect(page.getByText('Toast from the module')).toBeVisible()
    await expect(page.locator('iframe')).toHaveAttribute('sandbox', 'allow-scripts')
    await expect(page.locator('iframe')).toHaveAttribute('referrerpolicy', 'no-referrer')
    await expect(page.locator('iframe')).toHaveAttribute('title', /.+/)
  })
}

test('the module screen has no serious accessibility violation, and the breadcrumb names the entry', async ({ page }) => {
  await openPackagedShell(page, { script: GOOD_SCRIPT, path: `/m/${PACKAGED_ID}/hello`, stubs: stubHelloApi })
  await expect(page.frameLocator('iframe').getByText('result greeting 200 {"greeting":"Hello"}')).toBeVisible()

  await expectNoSeriousViolations(page)
  const crumbs = page.getByRole('navigation', { name: 'Breadcrumb' })
  await expect(crumbs).toContainText('Modules')
  await expect(crumbs).toContainText(PACKAGED_TEXT.en.menu)
})

test('the keyboard reaches the module entry, opens it, and focus is not lost into the frame', async ({ page }) => {
  await openPackagedShell(page, { script: GOOD_SCRIPT, stubs: stubHelloApi })

  const link = packagedMenuLink(page)
  await link.focus()
  await expect(link).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(`/m/${PACKAGED_ID}/hello`)
  await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT.en.title })).toBeVisible()
  // The frame is a tab stop of its own, after the shell's controls, reachable and named.
  const iframe = page.locator('iframe')
  await iframe.focus()
  await expect(iframe).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await expect(iframe).not.toBeFocused()
})

test('a deep link and a reload on a module screen open it, not the not-found screen', async ({ page }) => {
  await openPackagedShell(page, { script: GOOD_SCRIPT, path: `/m/${PACKAGED_ID}/hello`, stubs: stubHelloApi })
  await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT.en.title })).toBeVisible()

  await page.reload()
  await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT.en.title })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toHaveCount(0)

  // The inverse control: a screen the module did not declare IS not found.
  await page.goto(`/m/${PACKAGED_ID}/never-declared`)
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
})

test('a module asking for its second screen moves the shell there, and the frame starts on it', async ({ page }) => {
  await openPackagedShell(page, { script: NAVIGATE_SCRIPT, path: `/m/${PACKAGED_ID}/hello`, stubs: stubHelloApi })

  await expect(page).toHaveURL(`/m/${PACKAGED_ID}/other`)
  await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT.en.other })).toBeVisible()
  await expect(page.frameLocator('iframe').getByText('screen /other')).toBeVisible()
})

test('a module that is removed disappears from the menu and its route stops resolving after the catalogue is re-read', async ({
  page,
}) => {
  const { catalogue } = await openPackagedShell(page, { script: GOOD_SCRIPT, stubs: stubHelloApi })
  await expect(packagedMenuLink(page)).toBeVisible()

  catalogue.replace(() => {
    return [COMPILED_IN_MODULE]
  })
  const before = catalogue.reads()
  await page.evaluate(() => {
    window.dispatchEvent(new Event('focus'))
  })
  await expect.poll(() => {return catalogue.reads()}).toBeGreaterThan(before)

  await expect(packagedMenuLink(page)).toHaveCount(0)
  await expect(page.locator('#nav-group-modules')).toHaveCount(0)

  // The route is gone too: asking the router for the module's address now resolves to not-found.
  await page.evaluate((path) => {
    window.history.pushState({}, '', path)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, `/m/${PACKAGED_ID}/hello`)
  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
})

test('a module installed while the page is open appears after the focus re-read, without a reload', async ({ page }) => {
  const { catalogue } = await openPackagedShell(page, {
    script: GOOD_SCRIPT,
    stubs: stubHelloApi,
    module: () => {
      return packagedModule('en', { lifecycle: 'installed', spa: null, menu: [], routes: [] })
    },
  })
  // Installed but not yet composed (restart pending): nothing of it is drawn.
  await expect(packagedMenuLink(page)).toHaveCount(0)

  catalogue.replace((locale) => {
    return [COMPILED_IN_MODULE, packagedModule(locale)]
  })
  await page.evaluate(() => {
    window.dispatchEvent(new Event('focus'))
  })
  await expect(packagedMenuLink(page)).toBeVisible()
  await packagedMenuLink(page).click()
  await expect(page.getByRole('heading', { level: 1, name: PACKAGED_TEXT.en.title })).toBeVisible()
})

test('regaining focus re-reads the module catalogue and nothing else', async ({ page }) => {
  const { catalogue } = await openPackagedShell(page, { script: GOOD_SCRIPT, stubs: stubHelloApi })
  // The first read has been answered and drawn: a focus during it would be skipped, by design.
  await expect(packagedMenuLink(page)).toBeVisible()
  const requestsBefore = catalogue.apiRequests.length
  const readsBefore = catalogue.reads()

  await page.evaluate(() => {
    window.dispatchEvent(new Event('focus'))
  })
  await expect.poll(() => {return catalogue.reads()}).toBe(readsBefore + 1)

  // The notification bell re-reads its own unread figure on focus (its own, older behaviour); every
  // other request in the window between the focus and the answer was caused by the module wiring,
  // and it must be the local catalogue alone, never a marketplace or any other read.
  const caused = catalogue.apiRequests.slice(requestsBefore).filter((path) => {
    return !path.startsWith('/api/v1/notifications/')
  })
  expect(caused).toEqual(['/api/v1/modules'])
})

test('a degraded module lands on the upgrade page with the backend explanation, and its menu entry is locked', async ({
  page,
}) => {
  const degraded = (locale: SpecLocale): ReturnType<typeof packagedModule> => {
    return packagedModule(locale, {
      lifecycle: 'degraded',
      state: 'locked',
      spa: null,
      failureDisplay: 'The module needs a licence that is no longer valid.',
    })
  }
  await openPackagedShell(page, { script: GOOD_SCRIPT, module: degraded })

  const link = packagedMenuLink(page)
  // A locked entry leads to the page that says why; it never opens a screen.
  await expect(page.locator(`nav a[href="/upgrade/${PACKAGED_ID}"]`).first()).toBeVisible()
  await expect(link).toHaveCount(0)

  // A locked entry is `aria-disabled` but still navigable (UiNavLink), and Playwright treats
  // `aria-disabled` as not enabled, so the click is forced.
  await page.locator(`nav a[href="/upgrade/${PACKAGED_ID}"]`).first().click({ force: true })
  await expect(page).toHaveURL(`/upgrade/${PACKAGED_ID}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toBeVisible()
  await expect(page.getByText('The module needs a licence that is no longer valid.')).toBeVisible()

  // A deep link to its screen never frames it: the module is not running.
  await page.goto(`/m/${PACKAGED_ID}/hello`)
  await expect(page.locator('iframe')).toHaveCount(0)
})
