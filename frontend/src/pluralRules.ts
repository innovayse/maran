import type { AppLocale } from './types/app'

/**
 * How each locale chooses between the plural forms of a counted message.
 *
 * @remarks
 * **Why this file exists at all, and why it did not before.** Until the grant-repair
 * screen, no message in this panel counted anything: every string was either
 * invariant or interpolated a name. The first counted sentence was written as
 * `{count} grant(s)` / `{count} прав(а)`, which is a machine's way of avoiding the
 * question — `для 1 прав(а)` is not a phrase in Russian, and `rules/vue.md` forbids
 * machine text reaching the screen. A parenthesised suffix is the same defect as
 * printing a raw enum: it looks like text and reads like a placeholder.
 *
 * **Each locale's rule is a fact about its grammar, not a preference.**
 *
 * - English takes two forms, singular and plural, and `vue-i18n`'s own default
 *   already implements that — so english is deliberately absent from the map below.
 * - Russian takes three, and the boundary is not the number but its last digit,
 *   with the teens carved out: 1, 21, 31 take the singular (`право`); 2–4, 22–24
 *   take the few-form (`права`); everything else, the teens included, takes the
 *   many-form (`прав`). A rule written on the number alone gets 11 and 111 wrong,
 *   which is precisely the case a hand-written `count === 1 ? … : …` misses.
 * - Armenian takes ONE form: a noun after a numeral stays singular
 *   (`2 իրավունքի`, not a plural). It is in the map not because it needs choosing
 *   but because it needs NOT choosing — `vue-i18n`'s default rule would index past
 *   the single form it is given.
 *
 * **The shape of the contract.** `vue-i18n` calls the rule with the count and with
 * how many forms the message actually supplied, and expects a zero-based index into
 * them. Every rule below clamps to `choicesLength - 1`, so a message that supplies
 * fewer forms than its locale could use degrades to its last form instead of
 * rendering empty — the failure mode worth guarding, because an empty plural is
 * invisible in review and silent at runtime.
 */
export const pluralRules: Partial<
  Record<AppLocale, (choice: number, choicesLength: number) => number>
> = {
  /**
   * Russian: singular / few / many, decided by the last digit with the teens excluded.
   * @param choice The number the message is counting.
   * @param choicesLength How many forms this particular message supplied.
   * @returns The zero-based index of the form to render.
   */
  ru: (choice: number, choicesLength: number): number => {
    const count = Math.abs(choice)
    const lastDigit = count % 10
    const lastTwoDigits = count % 100
    const isTeen = lastTwoDigits >= 11 && lastTwoDigits <= 14

    if (!isTeen && lastDigit === 1) {
      return 0
    }

    if (!isTeen && lastDigit >= 2 && lastDigit <= 4) {
      return Math.min(1, choicesLength - 1)
    }

    return Math.min(2, choicesLength - 1)
  },

  /**
   * Armenian: one invariant form, because a noun after a numeral does not inflect for number.
   * @param _choice The number the message is counting; it cannot change the form.
   * @param choicesLength How many forms this particular message supplied.
   * @returns Always the first form, clamped for a message that supplied none beyond it.
   */
  hy: (_choice: number, choicesLength: number): number => {
    return Math.min(0, choicesLength - 1)
  },
}
