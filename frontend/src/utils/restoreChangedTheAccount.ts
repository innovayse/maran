/**
 * The two problem codes that mean a restore CHANGED the account before it stopped.
 *
 * `RestorePartial` is a restore that replaced some of what it set out to and rolled the rest back;
 * `RestoreTruncated` is the agent stopping mid-restore, so the panel cannot say how far it got.
 * Every other code the restore endpoint can answer with — a wrong confirmation, a backup that was
 * never completed, a backup that is not the caller's, a backup already running, a dropped agent
 * stream — is refused before anything is touched.
 *
 * This is a behaviour list, not a text list: the SPA branches on it to decide whether to offer the
 * operation again, and never to look up a message. The message is the backend's own, already
 * localized, and is rendered verbatim (rules/vue.md).
 */
const CODES_THAT_CHANGED_THE_ACCOUNT: readonly string[] = ['RestorePartial', 'RestoreTruncated']

/**
 * Whether a failed restore left the account changed.
 *
 * The distinction is the whole reason the screen branches at all. Every other failure means the
 * account is exactly as it was, and offering the operation again is correct; these two mean it is
 * in neither the state it was in nor the state it was going to, and a blind second attempt over a
 * half-replaced account is how the retry destroys what the first attempt left usable.
 *
 * An unrecognised code answers `false`, which is the permissive reading and the safe one HERE: the
 * caller uses this only to decide whether to keep offering a retry, and the account's real state is
 * the server's to report. It is never used to decide whether a restore may be attempted.
 * @param code The machine-stable problem code the panel answered with.
 * @returns True when the account has been changed and must not be restored over blindly.
 */
export const restoreChangedTheAccount = (code: string): boolean => {
  return CODES_THAT_CHANGED_THE_ACCOUNT.includes(code)
}
