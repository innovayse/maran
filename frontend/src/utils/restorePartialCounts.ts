import type { RestorePartial } from '../types/backup'

/**
 * Narrows the `restore` problem-extension member — the counts a partial restore's failure response
 * carries — into the typed shape the dialog renders, or `null` when the server sent none.
 *
 * `null` is the common case, not an error: every refusal that touched nothing, every truncated
 * stream, and every OLDER panel that predates the counts sends no member at all, and the dialog
 * renders its plain changed-account copy without them. The shape check is deliberately strict about
 * types and permissive about meaning: a member whose fields are not the advertised primitives is
 * treated as absent rather than rendered as garbage, but no verdict is recomputed from the numbers —
 * the counts are the server's statement and the SPA only shows them (rules/vue.md: "Data comes from
 * the backend; the SPA only displays it").
 * @param extensions The failed call's problem-extension members, from `ApiError.extensions`.
 * @returns The typed counts, or `null` when the server did not state any.
 */
export const restorePartialCounts = (
  extensions: Readonly<Record<string, unknown>>,
): RestorePartial | null => {
  const member = extensions['restore']
  if (typeof member !== 'object' || member === null) {
    return null
  }

  const candidate = member as Record<string, unknown>
  const filesRestored = candidate['filesRestored']
  const databasesRestored = candidate['databasesRestored']
  const databasesTotal = candidate['databasesTotal']

  // Finite-number checks rather than typeof alone: a NaN or Infinity smuggled into a count would
  // render as text in the dialog's sentence, which is worse than no sentence.
  if (
    typeof filesRestored !== 'boolean' ||
    typeof databasesRestored !== 'number' ||
    !Number.isFinite(databasesRestored) ||
    typeof databasesTotal !== 'number' ||
    !Number.isFinite(databasesTotal)
  ) {
    return null
  }

  return { filesRestored, databasesRestored, databasesTotal }
}
