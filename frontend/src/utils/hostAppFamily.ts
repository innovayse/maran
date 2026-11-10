import type { HostAppFamily } from '../types/hostApp'

/** The catalogue ids of the two site web servers; the closed set `docs/apps/README.md` names. */
const WEB_SERVER_IDS: readonly string[] = ['nginx', 'apache']

/** The prefix of a PHP runtime's catalogue id (`php-8.3`). */
const PHP_PREFIX = 'php-'

/** The prefix of a Node.js runtime's catalogue id (`nodejs-22`). */
const NODE_PREFIX = 'nodejs-'

/**
 * Tells which family a catalogue id belongs to. The ids are the panel's closed catalogue, so the
 * family is read from the id's shape and nothing else; an id of no known family answers `null`
 * and is left out of the wizard's pickers rather than guessed at.
 * @param id The catalogue id (`nginx`, `apache`, `php-<version>`, `nodejs-<major>`).
 * @returns The family, or `null` for an id this bundle does not recognise.
 */
export const hostAppFamily = (id: string): HostAppFamily | null => {
  if (WEB_SERVER_IDS.includes(id)) {
    return 'webServer'
  }
  if (id.startsWith(PHP_PREFIX)) {
    return 'php'
  }
  if (id.startsWith(NODE_PREFIX)) {
    return 'nodejs'
  }
  return null
}
