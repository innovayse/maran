import type { Page } from '@playwright/test'
import type { ResellerBranding, SaveResellerBrandingRequest } from '../../src/types/branding'

/** The shell's read: the branding the caller's own shell shows. */
const SHELL_PATTERN = /\/api\/v1\/accounts\/me\/branding(\?.*)?$/

/** The reseller's own branding: read it, replace it. */
const OWN_PATTERN = /\/api\/v1\/reseller\/branding(\?.*)?$/

/** What the panel answers when nothing is set: every member `null`, the panel's own look. */
export const NO_BRANDING: ResellerBranding = { displayName: null, accentColor: null, logo: null }

/** A real 1x1 PNG as a data URI, so an `<img>` loads it without a network request. */
export const TINY_PNG =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='

/**
 * Fulfils `GET /api/v1/accounts/me/branding`, the read the customer's shell makes on every sign-in.
 * @param page The Playwright page whose network the route is installed on.
 * @param branding What the stubbed panel says the caller's shell shows.
 * @returns A function reporting how many times the shell asked so far.
 */
export const stubShellBranding = async (
  page: Page,
  branding: ResellerBranding = NO_BRANDING,
): Promise<() => number> => {
  let calls = 0
  await page.route(SHELL_PATTERN, async (route) => {
    calls += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(branding) })
  })
  return () => {
    return calls
  }
}

/** What {@link stubResellerBranding} recorded and may be told to refuse with. */
export interface ResellerBrandingScript {
  /** The bodies of the saves the stubbed panel received. */
  saved: SaveResellerBrandingRequest[]
  /** When set, a save is refused with 400 and this sentence instead of being stored. */
  refusal?: string
}

/**
 * Stubs the reseller's own branding endpoint: a read answers what is stored, a save stores what was
 * sent exactly as the panel does (the colour in lower case, a blank name as no name) or is refused
 * with the scripted sentence.
 * @param page The Playwright page whose network the route is installed on.
 * @param script The saves received and the refusal to answer with.
 * @param stored What the panel holds to begin with.
 * @returns Resolves once the route is installed.
 */
export const stubResellerBranding = async (
  page: Page,
  script: ResellerBrandingScript,
  stored: ResellerBranding = NO_BRANDING,
): Promise<void> => {
  let current = stored
  await page.route(OWN_PATTERN, async (route) => {
    const request = route.request()
    if (request.method() === 'PUT') {
      if (script.refusal !== undefined) {
        await route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: script.refusal, detail: script.refusal, status: 400, code: 'BrandingAccentColorLowContrast' }),
        })
        return
      }
      const body = request.postDataJSON() as SaveResellerBrandingRequest
      script.saved.push(body)
      current = {
        displayName: body.displayName?.trim() ? body.displayName.trim() : null,
        accentColor: body.accentColor === null ? null : body.accentColor.toLowerCase(),
        logo: body.logo,
      }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
}
