import type { Page } from '@playwright/test'
import type { PhpExtension } from '../../src/types/phpVersion'

/** One switch request the stubbed panel received. */
export interface PhpExtensionSwitchRequest {
  /** The extension named in the path. */
  name: string
  /** The state the body asked for. */
  enabled: boolean
}

/** What {@link stubPhpExtensions} installed, for a spec to read and steer. */
export interface PhpExtensionsStub {
  /** The extensions the stubbed server holds; a successful switch changes the matching row. */
  readonly extensions: PhpExtension[]
  /** Every switch request received, in order. */
  readonly requests: PhpExtensionSwitchRequest[]
}

/** The path every switch request is sent to, whatever the version and extension. */
const SWITCH_PATTERN = '**/api/v1/sites/php-versions/*/extensions/*'

/** The path the extension list is read from, whatever the version. */
const LIST_PATTERN = '**/api/v1/sites/php-versions/*/extensions'

/**
 * Fulfils the extension list and the switch of a PHP version the way the panel does: the list
 * answers the rows as held, and a switch changes the held row and answers the extensions read back
 * afterwards. A switch is refused with the given sentence when `refusal` is set, and the row stays as
 * it was.
 * @param page The Playwright page whose network the routes are installed on.
 * @param extensions The extensions the stubbed server starts with.
 * @param refusal The panel's own sentence to refuse every switch with, or `null` to accept them.
 * @returns The stub: the held rows and the requests received.
 */
export const stubPhpExtensions = async (
  page: Page,
  extensions: PhpExtension[],
  refusal: string | null = null,
): Promise<PhpExtensionsStub> => {
  const requests: PhpExtensionSwitchRequest[] = []

  await page.route(LIST_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(extensions) })
  })

  await page.route(SWITCH_PATTERN, async (route) => {
    const name = decodeURIComponent(new URL(route.request().url()).pathname.split('/').pop() ?? '')
    const body = route.request().postDataJSON() as { enabled: boolean }
    requests.push({ name, enabled: body.enabled })

    if (refusal !== null) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'PhpExtensionToggleFailed', title: 'Refused', detail: refusal }),
      })
      return
    }

    const row = extensions.find((candidate) => {
      return candidate.name === name
    })
    if (row) {
      row.enabled = body.enabled
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(extensions) })
  })

  return { extensions, requests }
}
