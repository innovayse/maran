import type { Page } from '@playwright/test'

/**
 * Answers `GET <path>` and `GET <path>?accountId=<id>` the way the panel's list endpoints do since
 * #223: the rows narrowed to the named account, or every row without the parameter. Matched with a
 * regular expression because a glob ending at the path does not match a URL carrying a query.
 *
 * Installed AFTER a module's own stub, so it takes the list read (Playwright prefers the most
 * recently registered route) while the module stub keeps answering everything else.
 * @param page The Playwright page whose network the route is installed on.
 * @param path The collection path, such as `/api/v1/sites`.
 * @param rows Every row the stub knows, each carrying its owning account.
 * @returns A function reporting the `accountId` each list read carried so far, `''` for none.
 */
export const stubAccountFilteredList = async (
  page: Page,
  path: string,
  rows: ReadonlyArray<{ accountId: string }>,
): Promise<() => readonly string[]> => {
  const requested: string[] = []
  const escaped = path.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
  await page.route(new RegExp(`${escaped}(\\?.*)?$`), async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    const accountId = new URL(route.request().url()).searchParams.get('accountId') ?? ''
    requested.push(accountId)
    const body =
      accountId === ''
        ? rows
        : rows.filter((row) => {
            return row.accountId === accountId
          })
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return () => {
    return requested
  }
}
