import { MAX_PATH_LENGTH } from '../constants/bridgeLimits'

/**
 * Checks a module-relative path for `navigate`. Whether it is one of the module's declared routes is
 * the shell's decision; this only refuses what can never be a route.
 *
 * @param path The path the module wants to open, for example `/hello`.
 * @returns Null when acceptable, otherwise the reason it is refused.
 */
export const checkNavigatePath = (path: string): string | null => {
  if (path.length === 0 || path.length > MAX_PATH_LENGTH || !path.startsWith('/') || path.startsWith('//')) {
    return 'The path must be a module-relative path starting with a single "/".'
  }

  if (path.includes('..') || path.includes('\\')) {
    return 'The path must not contain ".." or a backslash.'
  }

  return null
}
