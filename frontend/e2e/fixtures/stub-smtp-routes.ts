import type { Page } from '@playwright/test'
import type { SmtpSecurityChoice, SmtpSettings } from '../../src/types/smtpSettings'

/**
 * Settings with a password stored, as the panel reports them: `hasPassword` is
 * true and there is no field anywhere that could carry the value.
 */
export const storedSmtpSettings: SmtpSettings = {
  host: 'smtp.example.net',
  port: 587,
  security: 'startTls',
  securityName: 'STARTTLS (upgrade the connection)',
  username: 'panel@example.net',
  hasPassword: true,
  fromAddress: 'panel@example.net',
  fromName: 'Maran',
  alertRecipient: 'ops@example.net',
  updatedAt: '2026-09-01T10:00:00+00:00',
}

/** The protection modes the panel lists, in the order it lists them, named as it names them in English. */
export const smtpSecurityModes: SmtpSecurityChoice[] = [
  { security: 'none', securityName: 'No encryption' },
  { security: 'startTls', securityName: 'STARTTLS (upgrade the connection)' },
  { security: 'implicitTls', securityName: 'Implicit TLS (encrypted from the start)' },
]

/**
 * Fulfils `GET /api/v1/notifications/smtp` with a chosen answer, and
 * `GET /api/v1/notifications/smtp/security-modes` with the modes the panel lists.
 *
 * The body is typed loosely on purpose: one spec answers with a field the real
 * DTO does not have, to prove the screen renders nothing it was not written to
 * render.
 * @param page The Playwright page whose network the route is installed on.
 * @param body The settings the panel reports.
 * @returns Resolves once the route is installed.
 */
export const stubSmtpSettings = async (page: Page, body: unknown): Promise<void> => {
  await page.route('**/api/v1/notifications/smtp', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  await page.route('**/api/v1/notifications/smtp/security-modes', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(smtpSecurityModes) })
  })
}

/**
 * Fulfils `POST /api/v1/notifications/smtp/test` with a refusal carrying the mail
 * server's own words in the RFC 7807 `detail`.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The refusal text the panel relays.
 * @returns Resolves once the route is installed.
 */
export const stubTestMailRefused = async (page: Page, detail: string): Promise<void> => {
  await page.route('**/api/v1/notifications/smtp/test', async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'SmtpSendFailed', detail }),
    })
  })
}
