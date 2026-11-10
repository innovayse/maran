import type { HostAppInUse } from '../types/hostApp'

/** The problem extension member the panel lists the using sites under (`AppsInUse`). */
const SITES_MEMBER = 'sites'

/**
 * Reads the sites that use an app out of a refusal's extension members: the panel sends
 * `sites: { total, domains }`, the count of all of them and the domains of the first few.
 *
 * The members are `unknown` on purpose (`ApiError.extensions`), so the shape is checked here: a
 * member of any other shape yields no sites, and the refusal's own sentence is still shown.
 * @param extensions The refusal's problem-extension members.
 * @returns The domains in the order the panel sent them and the total; empty and zero when the panel named none.
 */
export const hostAppInUseSites = (
  extensions: Readonly<Record<string, unknown>>,
): Pick<HostAppInUse, 'sites' | 'total'> => {
  const member = extensions[SITES_MEMBER]
  if (typeof member !== 'object' || member === null) {
    return { sites: [], total: 0 }
  }
  const { domains, total } = member as { domains?: unknown; total?: unknown }
  const sites = Array.isArray(domains)
    ? domains.filter((entry): entry is string => {
        return typeof entry === 'string'
      })
    : []
  return { sites, total: typeof total === 'number' ? total : sites.length }
}
