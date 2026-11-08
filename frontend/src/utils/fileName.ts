/**
 * The one client-side reading of what a single file or folder name may be, for the dialogs that ask
 * a person to type one (new entry, rename, archive name).
 *
 * This is advice that saves a round trip, nothing more. It mirrors the panel's own name rule and
 * the server re-validates every name, answering with its own already-localized message; the agent
 * alone decides what stays inside a home (rules/security.md item 2). A name that passes here is
 * therefore not "safe", it is merely not obviously wrong.
 */

/** The longest a name may be, in bytes, the limit of one path segment on the supported systems. */
const MAX_NAME_BYTES = 255

/** Why a typed name was refused; each reason has its own translated sentence in the dialogs. */
export type FileNameProblem = 'required' | 'separator' | 'dots' | 'control' | 'tooLong'

/**
 * Reads a typed name.
 * @param name The name as typed, already trimmed by the caller.
 * @returns The first reason the name is not a usable single segment, or `null` when none is evident.
 */
export const fileNameProblem = (name: string): FileNameProblem | null => {
  if (name === '') {
    return 'required'
  }
  if (name.includes('/') || name.includes('\\')) {
    return 'separator'
  }
  if (name === '.' || name === '..') {
    return 'dots'
  }
  if (/\p{Cc}/u.test(name)) {
    return 'control'
  }
  return new TextEncoder().encode(name).length > MAX_NAME_BYTES ? 'tooLong' : null
}
