import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { expect, test } from '@playwright/test'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubSmtpSettings, storedSmtpSettings } from '../fixtures/stub-smtp-routes'
import { setPersistedLocale } from '../fixtures/set-locale'

// The class of defect this file closes, rather than the one instance of it.
//
// Three times now a module the licence INCLUDES has been linked to the upgrade wall — `identity`,
// `ssl`, and then `notifications`, which was still walled after the fix that named the first two,
// telling an operator the module was outside their licence and, one line below, that its tier was
// "included in the distribution". Each time the repair was an entry in `LANDING_ROUTES`, and each
// time nothing stopped the next module from being forgotten: the map is written in the SPA, the
// catalogue is composed in the backend, and no artefact related the two.
//
// So this reads the catalogue's OWN source — `ModuleRegistry.All`, which is exactly what
// `ModulesEndpoint.DescribeModules` enumerates, and each module's `Manifest.Id`, which is exactly
// the `name` field the endpoint reports — serves that list to a real browser, and follows the
// sidebar. A module registered on the backend with no landing route fails here on the day it is
// registered, with nobody having remembered anything.
//
// UNOBSERVED HERE: this reads the C# that BUILDS the catalogue, not an HTTP response from a running
// panel. It therefore cannot see a licence making a module `isEnabled: false` (which is what the
// upgrade wall is honestly for), nor a `Manifest.Id` computed at runtime rather than written as a
// literal — the parse below refuses a manifest it cannot read a literal id out of, rather than
// silently dropping the module from the list it checks.

/** Repository root, resolved from this spec's own location rather than from the runner's cwd. */
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

/** The explicit registry of compiled-in modules — the list `GET /api/v1/modules` enumerates. */
const REGISTRY_FILE = join(REPO_ROOT, 'backend', 'src', 'Maran.Host', 'Modules', 'ModuleRegistry.cs')

/** Where a module's own project lives, keyed by the C# type prefix the registry constructs. */
const MODULE_DIR = join(REPO_ROOT, 'backend', 'src', 'Maran.Modules')

/** A `new XModule()` entry in the registry's collection expression. */
const REGISTERED_MODULE = /new\s+(\w+)Module\(\)/g

/** The `Id: "…"` argument of a manifest's construction — the module's stable machine name. */
const MANIFEST_ID = /\bId:\s*"([^"]+)"/

/**
 * A module id no backend composes, used as this check's inverse control: it MUST reach the upgrade
 * wall. Without it a sidebar that had stopped rendering hrefs at all — or a selector that had
 * stopped matching them — would report every real module as fine.
 */
const UNKNOWN_MODULE = 'zz-not-a-real-module'

/**
 * Reads the module ids the backend actually composes, in registration order.
 * @returns One machine name per registered module.
 */
const composedModuleIds = (): string[] => {
  const registry = readFileSync(REGISTRY_FILE, 'utf8')
  const prefixes = [...registry.matchAll(REGISTERED_MODULE)].map((match) => {
    return match[1]
  })
  // A registry this parse cannot read is a broken check, not an empty one: an empty list would let
  // every assertion below pass while observing nothing.
  expect(prefixes.length, `no modules parsed out of ${REGISTRY_FILE}`).toBeGreaterThan(0)

  return prefixes.map((prefix) => {
    const manifest = readFileSync(join(MODULE_DIR, prefix, `${prefix}Manifest.cs`), 'utf8')
    const id = MANIFEST_ID.exec(manifest)
    expect(id, `no literal Id in ${prefix}Manifest.cs`).not.toBeNull()
    return id === null ? '' : id[1]
  })
}

test('every module the backend composes has a sidebar destination that is not the upgrade wall', async ({
  page,
}) => {
  const ids = composedModuleIds()
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    ...ids.map((name) => {
      // A distinctive label per module, so a failure names the module rather than a position.
      return { name, displayName: `Module ${name}`, tier: 'included', isEnabled: true }
    }),
    { name: UNKNOWN_MODULE, displayName: `Module ${UNKNOWN_MODULE}`, tier: 'included', isEnabled: true },
  ])

  await page.goto('/')

  // The inverse control first: the one module nothing has a screen for DOES reach the wall, which
  // is what proves the assertion below can see an upgrade link at all.
  await expect(page.getByRole('link', { name: `Module ${UNKNOWN_MODULE}` })).toHaveAttribute(
    'href',
    `/upgrade/${UNKNOWN_MODULE}`,
  )

  // And then the property: that link is the ONLY one. The value, not a bound — a real module
  // reaching the wall shows up here as a second href, named in the failure.
  const walled = await page.locator('a[href^="/upgrade/"]').evaluateAll((links) => {
    return links.map((link) => {
      return link.getAttribute('href')
    })
  })
  expect(walled).toEqual([`/upgrade/${UNKNOWN_MODULE}`])
})

test('the notifications module reaches the outgoing-mail screen from the sidebar in Russian', async ({
  page,
}) => {
  // The instance the class was found through, asserted where it was seen: a Russian panel, whose
  // sidebar entry «Уведомления» led to a wall reading "not included in your licence" directly above
  // "Licence tier: included in the distribution".
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubSmtpSettings(page, storedSmtpSettings)
  await stubModules(page, [
    {
      name: 'notifications',
      displayName: 'Уведомления',
      tierDisplayName: 'Входит в поставку',
      tier: 'included',
      isEnabled: true,
    },
  ])

  await page.goto('/')
  await page.getByRole('link', { name: 'Уведомления' }).click()

  // The destination, not the fact that a navigation happened: an href that merely is not
  // `/upgrade/...` could still name a route nothing serves.
  await expect(page).toHaveURL('/settings/smtp')
  await expect(page.getByRole('heading', { level: 1, name: 'Исходящая почта' })).toBeVisible()
})
