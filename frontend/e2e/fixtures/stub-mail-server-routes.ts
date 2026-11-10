import type { Page } from '@playwright/test'
import type { MailServerStatus } from '../../src/types/mailServer'

/** What the stubbed panel states a server is turned on with. */
export const MAIL_STARTING_VALUES: MailServerStatus['startingValues'] = {
  pop3Enabled: false,
  antivirusEnabled: false,
  allowExternalForwarding: false,
  maxMessageMib: 50,
  spamGreylistScore: 4,
  spamAddHeaderScore: 6,
  spamRejectScore: 15,
}

/** A server where mail was never configured: no settings row. */
export const MAIL_OFF: MailServerStatus = { settings: null, startingValues: MAIL_STARTING_VALUES }

/** A server whose mail is on. */
export const MAIL_ON: MailServerStatus = {
  settings: { enabled: true, hostname: 'mail.example.com' },
  startingValues: MAIL_STARTING_VALUES,
}

/** What the stubbed mail server lets a spec read back. */
export interface MailServerStub {
  /** The bodies of every turn-on request so far, oldest first. */
  enables: () => readonly Record<string, unknown>[]
}

/**
 * Serves the administrator's mail server status and its turn-on action. Turning on moves the held
 * status to {@link MAIL_ON} under the host name sent, the way the panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param initial The status the server starts with.
 * @returns A recorder of the turn-on requests.
 */
export const stubMailServer = async (page: Page, initial: MailServerStatus): Promise<MailServerStub> => {
  let held = initial
  const enables: Record<string, unknown>[] = []
  await page.route(/\/api\/v1\/mail-server$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) })
  })
  await page.route(/\/api\/v1\/mail-server\/enable$/, async (route) => {
    const body = route.request().postDataJSON() as Record<string, unknown>
    enables.push(body)
    held = { settings: { enabled: true, hostname: String(body.hostname) }, startingValues: MAIL_STARTING_VALUES }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) })
  })
  return {
    enables: (): readonly Record<string, unknown>[] => {
      return enables
    },
  }
}
