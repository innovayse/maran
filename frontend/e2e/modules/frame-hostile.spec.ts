import { expect, test } from '../fixtures/guarded-test'
import {
  FLOOD_SCRIPT,
  GOOD_SCRIPT,
  MALFORMED_SCRIPT,
  ONE_WRONG_NONCE_THEN_RIGHT_SCRIPT,
  OUT_OF_ROUTE_SCRIPT,
  OVERSIZED_API_SCRIPT,
  OVERSIZED_SCRIPT,
  SECOND_READY_SCRIPT,
  SILENT_SCRIPT,
  THROWS_AFTER_READY_SCRIPT,
  THROWS_ON_LOAD_SCRIPT,
  UNKNOWN_TYPE_SCRIPT,
  WRONG_NONCE_SCRIPT,
} from '../fixtures/module-frame-scripts'
import { openPackagedShell, packagedMenuLink } from '../fixtures/open-packaged-module'
import { PACKAGED_ID, PACKAGED_TEXT } from '../fixtures/stub-packaged-module'
import { test as unguardedTest, type Page } from '@playwright/test'

// Issue #185, the hostile half. A packaged module is third-party code: it may throw, hang, lie or
// flood. Whatever it does, the shell, the menu and every core page keep working, and the module's
// own screen shows the unavailable state with a retry. Every case below ends in the same two
// assertions (the unavailable panel, and a shell that still works), and the accepting controls
// prove those assertions can fail: a good module and a recoverable mistake do NOT show the panel.

/** The panel's sentence for a module that sent messages it does not accept. */
const STOPPED = 'The module was stopped because it sent messages the panel does not accept.'

/** The panel's sentence for a module that did not start in time. */
const DID_NOT_START = 'The module did not start in time.'

/** The title of the unavailable panel, with the English module name. */
const UNAVAILABLE_TITLE = `${PACKAGED_TEXT.en.name} is unavailable`

/** How far the clock jumps to pass the handshake deadline (10 s) with room to spare. */
const PAST_THE_DEADLINE_MS = 11_000

/**
 * Proves the rest of the panel is untouched: a core page opens from the menu, the module's own entry
 * is still there, and the document has its one landmark and heading.
 * @param page The page under test.
 * @returns Resolves once the core page is on screen.
 */
const expectShellStillWorks = async (page: Page): Promise<void> => {
  await page.locator('nav a[href="/"]').first().click()
  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
  await expect(packagedMenuLink(page)).toBeVisible()
  await expect(page.getByRole('main')).toHaveCount(1)
}

/**
 * Opens the module's screen from the menu.
 * @param page The page under test.
 * @returns Resolves once the screen's route is current.
 */
const openModuleScreen = async (page: Page): Promise<void> => {
  await packagedMenuLink(page).click()
  await expect(page).toHaveURL(`/m/${PACKAGED_ID}/hello`)
}

/**
 * Answers the sample module's greeting endpoint the way its backend would.
 * @param page The page under test.
 * @returns Resolves once installed.
 */
const stubGreeting = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/hello/greeting', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ greeting: 'Hello' }) })
  })
}

/**
 * Answers the sample module's ping endpoint the way its backend would; only the flooding bundle
 * calls it, and the shell relays at most thirty of its two hundred calls.
 * @param page The page under test.
 * @returns Resolves once installed.
 */
const stubPing = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/hello/ping', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ pong: true }) })
  })
}

/** The cases that need no clock: the module is stopped by what it sends. */
const STOPPED_BY_MESSAGES: ReadonlyArray<readonly [string, string]> = [
  ['sends malformed messages', MALFORMED_SCRIPT],
  ['sends messages of an unknown type', UNKNOWN_TYPE_SCRIPT],
  ['sends toasts over the size limit', OVERSIZED_SCRIPT],
  ['sends API bodies over the size limit', OVERSIZED_API_SCRIPT],
  ['floods the API relay', FLOOD_SCRIPT],
  ['announces itself again after the handshake', SECOND_READY_SCRIPT],
  ['announces itself with the wrong nonce', WRONG_NONCE_SCRIPT],
  ['navigates to screens it never declared', OUT_OF_ROUTE_SCRIPT],
]

for (const [name, script] of STOPPED_BY_MESSAGES) {
  test(`a module that ${name} is stopped and the rest of the panel keeps working`, async ({ page }) => {
    await openPackagedShell(page, { script, stubs: stubPing })
    await openModuleScreen(page)

    await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
    await expect(page.getByText(STOPPED, { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible()
    // The frame is removed with the panel, not merely hidden.
    await expect(page.locator('iframe')).toHaveCount(0)
    await expectShellStillWorks(page)
  })
}

// The two specs below are about a bundle that throws: the uncaught error inside the frame IS the
// hostile behaviour under test, and the guarded `test` fails a spec on any uncaught page error, so
// they run on the plain `test` (the frame's own error is the only thing they let through; they
// stub every read the shell makes all the same).
unguardedTest('a bundle that throws while loading never starts, and the deadline stops waiting for it', async ({ page }) => {
  await page.clock.install()
  await openPackagedShell(page, { script: THROWS_ON_LOAD_SCRIPT })
  await openModuleScreen(page)
  // Not yet: the shell waits for the handshake before it gives up.
  await expect(page.getByText(UNAVAILABLE_TITLE)).toHaveCount(0)

  await page.clock.fastForward(PAST_THE_DEADLINE_MS)

  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
  await expect(page.getByText(DID_NOT_START, { exact: false })).toBeVisible()
  await expectShellStillWorks(page)
})

test('a bundle that never sends ready is given up on after the deadline', async ({ page }) => {
  await page.clock.install()
  await openPackagedShell(page, { script: SILENT_SCRIPT })
  await openModuleScreen(page)
  await page.clock.fastForward(PAST_THE_DEADLINE_MS)

  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
  await expect(page.getByText(DID_NOT_START, { exact: false })).toBeVisible()
  await expectShellStillWorks(page)
})

test('a retry mounts a fresh frame with a fresh nonce and recovers when the bundle now works', async ({ page }) => {
  const { frame } = await openPackagedShell(page, { script: MALFORMED_SCRIPT, stubs: stubGreeting })
  await openModuleScreen(page)
  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
  const loadsBefore = frame.documentLoads()
  expect(loadsBefore).toBeGreaterThan(0)

  frame.setScript(GOOD_SCRIPT)
  await page.getByRole('button', { name: 'Try again' }).click()

  const iframe = page.locator('iframe')
  await expect(iframe).toHaveAttribute('src', /#[0-9a-f]{32}$/)
  await expect(page.frameLocator('iframe').getByText('result greeting 200 {"greeting":"Hello"}')).toBeVisible()
  await expect(page.getByText(UNAVAILABLE_TITLE)).toHaveCount(0)
  expect(frame.documentLoads()).toBe(loadsBefore + 1)
})

test('a retry that fails again shows the unavailable panel again', async ({ page }) => {
  const { frame } = await openPackagedShell(page, { script: MALFORMED_SCRIPT })
  await openModuleScreen(page)
  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
  const loadsBefore = frame.documentLoads()

  await page.getByRole('button', { name: 'Try again' }).click()

  await expect.poll(() => {return frame.documentLoads()}).toBe(loadsBefore + 1)
  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()
  await expectShellStillWorks(page)
})

test('a flooding module reaches the API no more than the rate limit allows', async ({ page }) => {
  let served = 0
  await openPackagedShell(page, {
    script: FLOOD_SCRIPT,
    stubs: async (stubbed): Promise<void> => {
      await stubbed.route('**/api/v1/hello/ping', async (route) => {
        served += 1
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ pong: true }) })
      })
    },
  })
  await openModuleScreen(page)
  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()

  // 200 were sent; at most 30 inside one second may be relayed, and the third refusal ends the mount.
  expect(served).toBeLessThanOrEqual(30)
})

test('an API body over the limit never reaches the server', async ({ page }) => {
  let served = 0
  await openPackagedShell(page, {
    script: OVERSIZED_API_SCRIPT,
    stubs: async (stubbed): Promise<void> => {
      await stubbed.route('**/api/v1/hello/items', async (route) => {
        served += 1
        await route.fulfill({ status: 201, contentType: 'application/json', body: '{}' })
      })
    },
  })
  await openModuleScreen(page)
  await expect(page.getByText(UNAVAILABLE_TITLE)).toBeVisible()

  expect(served).toBe(0)
})

test('accepting control: one wrong nonce is counted but the right one still completes the handshake', async ({
  page,
}) => {
  await openPackagedShell(page, { script: ONE_WRONG_NONCE_THEN_RIGHT_SCRIPT })
  await openModuleScreen(page)

  await expect(page.frameLocator('iframe').getByText('ready after one wrong nonce, route:/hello')).toBeVisible()
  await expect(page.getByText(UNAVAILABLE_TITLE)).toHaveCount(0)
})

unguardedTest('accepting control: a module that throws inside its own handler after the handshake keeps its screen', async ({
  page,
}) => {
  await openPackagedShell(page, { script: THROWS_AFTER_READY_SCRIPT })
  await openModuleScreen(page)

  // The throw is the module's own business, inside its frame; the shell did not see a violation.
  await expect(page.locator('iframe')).toBeVisible()
  await expect(page.getByText(UNAVAILABLE_TITLE)).toHaveCount(0)
  await expectShellStillWorks(page)
})

test('accepting control: a message to the window from anything but the frame is not held against the frame', async ({
  page,
}) => {
  await openPackagedShell(page, { script: GOOD_SCRIPT, stubs: stubGreeting })
  await openModuleScreen(page)
  // Three would end the mount if they were counted; they come from the page itself, not the frame.
  await page.evaluate(() => {
    for (let index = 0; index < 5; index++) {
      window.postMessage({ v: 1, type: 'ready', nonce: 'not-the-nonce' }, '*')
      window.postMessage('garbage', '*')
    }
  })

  await expect(page.frameLocator('iframe').getByText('result greeting 200 {"greeting":"Hello"}')).toBeVisible()
  await expect(page.getByText(UNAVAILABLE_TITLE)).toHaveCount(0)
})
