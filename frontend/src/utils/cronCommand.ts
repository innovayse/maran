import { hasControlCharacter, hasEdgeWhitespace } from './cronText'

/**
 * The client-side mirror of the panel's `CronCommandRule`: what this SPA accepts as one cron
 * command line.
 *
 * **A mirror, and only a mirror.** The module re-validates the command and the agent re-validates
 * it again; checking here is what lets an operator be told what is wrong with the line they typed
 * instead of being handed a refusal from a process they cannot see (rules/vue.md).
 *
 * The alphabet is a short list of refusals rather than a permitted set, and that is the module's
 * decision faithfully reproduced. The command never reaches the crontab — the agent writes it to a
 * per-entry file under the account's home and the installed crontab line runs that file — so the
 * two characters a crontab line genuinely cannot carry are ordinary text here. Refusing a percent
 * sign or a hash would refuse working commands for a danger that does not exist at this position.
 */

/**
 * The most bytes a command may be, matching the module's own ceiling.
 *
 * Measured in UTF-8 BYTES, not in UTF-16 code units, because the agent measures the bytes it
 * writes. A command of three thousand emoji is three thousand characters and twelve thousand bytes:
 * counted as characters it would pass here and be refused after the operator had been told their
 * entry was accepted.
 */
const MAXIMUM_LENGTH_IN_BYTES = 4096



/** Measures a string the way the module does — in UTF-8 bytes. */
const encoder = new TextEncoder()

/**
 * Whether a candidate is one acceptable cron command line.
 *
 * Surrounding whitespace is refused rather than trimmed. The command is stored verbatim and
 * compared verbatim when the agent decides whether an entry duplicates one already installed, so a
 * leading space and a trailing space must not become two spellings of one command — and trimming
 * silently would show the operator something other than what they typed.
 * @param candidate The command line as the operator typed it.
 * @returns True when it is a non-empty, bounded, single line with no control character and no
 * leading or trailing whitespace.
 */
export const isOneCronCommandLine = (candidate: string): boolean => {
  if (candidate.length === 0) {
    return false
  }

  if (encoder.encode(candidate).length > MAXIMUM_LENGTH_IN_BYTES) {
    return false
  }

  // What a FILE holding exactly one line cannot carry. Whitespace is not in this class — a command
  // is full of spaces — so the edges are a separate check, exactly as they are in the module.
  if (hasControlCharacter(candidate)) {
    return false
  }

  return !hasEdgeWhitespace(candidate)
}
