/**
 * Pure helpers for the relative paths the file manager passes around: `/`-separated, relative to
 * the account's home, the empty string meaning the home itself.
 *
 * These only split and join text so a screen can draw a path bar and build a destination. They make
 * NO claim about containment: a path that looks safe here is still validated by the server, and the
 * agent alone decides what stays inside the home. A value typed by a person is sent as typed.
 */

/** The separator between segments of a relative path. */
export const PATH_SEPARATOR = '/'

/**
 * Splits a relative path into its segments. Empty segments (a doubled or trailing separator) are
 * dropped, so the home and `a//b/` give `[]` and `['a', 'b']`.
 * @param path A relative path.
 * @returns The segments in order; empty for the home.
 */
export const pathSegments = (path: string): string[] => {
  return path.split(PATH_SEPARATOR).filter((segment) => {return segment !== ''})
}

/**
 * Joins segments into a relative path.
 * @param segments The segments, in order; empty ones are skipped.
 * @returns The path; empty for no segments.
 */
export const joinPath = (...segments: readonly string[]): string => {
  return segments.filter((segment) => {return segment !== ''}).join(PATH_SEPARATOR)
}

/**
 * Names the entry called `name` inside the folder `directory`.
 * @param directory The folder, relative to the home; empty for the home.
 * @param name The entry's own name.
 * @returns The entry's path.
 */
export const childPath = (directory: string, name: string): string => {
  return joinPath(directory, name)
}

/**
 * The folder that holds a path.
 * @param path A relative path.
 * @returns The path without its last segment; empty for the home and for an entry directly in it.
 */
export const parentPath = (path: string): string => {
  return pathSegments(path).slice(0, -1).join(PATH_SEPARATOR)
}

/**
 * The last segment of a path.
 * @param path A relative path.
 * @returns The entry's own name; empty for the home.
 */
export const baseName = (path: string): string => {
  const segments = pathSegments(path)
  return segments.length === 0 ? '' : segments[segments.length - 1]
}

/**
 * The path of each ancestor of a path, outermost first, ending with the path itself, for a path
 * bar whose every part links to its folder.
 * @param path A relative path.
 * @returns One `{ name, path }` per segment; empty for the home.
 */
export const pathTrail = (path: string): { name: string; path: string }[] => {
  const segments = pathSegments(path)
  return segments.map((name, index) => {
    return { name, path: segments.slice(0, index + 1).join(PATH_SEPARATOR) }
  })
}
