import type { Page } from '@playwright/test'
import type { CronChoice, CronOptions } from '../../src/types/cronOptions'

/** The languages the panel ships, which is what the stub names its lists in. */
type Language = 'en' | 'ru' | 'hy'

/** What one language calls each named list; the numeric lists need no words. */
interface NamedLists {
  /** Frequency names, in the order the builder offers them. */
  frequencies: readonly [string, string, string, string, string]
  /** Weekday names, Sunday first. */
  weekdays: readonly [string, string, string, string, string, string, string]
  /** Outcome names: succeeded, failed, stopped. */
  outcomes: readonly [string, string, string]
}

/** The frequency values the panel sends, in order. */
const FREQUENCY_VALUES = ['everyMinute', 'hourly', 'daily', 'weekly', 'monthly'] as const

/** The outcome values the panel sends, in order. */
const OUTCOME_VALUES = ['succeeded', 'failed', 'stopped'] as const

/** The words each language gives the named lists, as the backend's resource files do. */
const NAMES: Readonly<Record<Language, NamedLists>> = {
  en: {
    frequencies: ['Every minute', 'Every hour', 'Every day', 'Every week', 'Every month'],
    weekdays: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
    outcomes: ['Succeeded', 'Failed', 'Stopped'],
  },
  ru: {
    frequencies: ['Каждую минуту', 'Каждый час', 'Каждый день', 'Каждую неделю', 'Каждый месяц'],
    weekdays: ['воскресенье', 'понедельник', 'вторник', 'среда', 'четверг', 'пятница', 'суббота'],
    outcomes: ['Успешно', 'Ошибка', 'Остановлен'],
  },
  hy: {
    frequencies: ['Ամեն րոպե', 'Ամեն ժամ', 'Ամեն օր', 'Ամեն շաբաթ', 'Ամեն ամիս'],
    weekdays: ['կիրակի', 'երկուշաբթի', 'երեքշաբթի', 'չորեքշաբթի', 'հինգշաբթի', 'ուրբաթ', 'շաբաթ'],
    outcomes: ['Հաջողված', 'Ձախողված', 'Կանգնեցված'],
  },
}

/**
 * Builds an inclusive run of numbers the way the panel lists them: the unpadded value, and a label
 * padded to two digits when asked.
 * @param first The first number.
 * @param last The last number.
 * @param pad Whether the label is padded to two digits.
 * @returns One choice per number.
 */
const numbers = (first: number, last: number, pad: boolean): CronChoice[] => {
  return Array.from({ length: last - first + 1 }, (_, index) => {
    const number = first + index
    return { value: String(number), label: pad ? String(number).padStart(2, '0') : String(number) }
  })
}

/**
 * Chooses the language a request asked in, from the first tag of its `Accept-Language` header.
 * @param header The header value, or `undefined` when the request carried none.
 * @returns The language to answer in; English when the header names none the panel ships.
 */
const languageOf = (header: string | undefined): Language => {
  const first = (header ?? '').split(',')[0]?.trim().slice(0, 2)
  return first === 'ru' || first === 'hy' ? first : 'en'
}

/**
 * The lists the panel would answer in one language.
 * @param language The language to name them in.
 * @returns The lists.
 */
const optionsIn = (language: Language): CronOptions => {
  const names = NAMES[language]
  return {
    frequencies: FREQUENCY_VALUES.map((value, index) => {
      return { value, label: names.frequencies[index] ?? value }
    }),
    weekdays: names.weekdays.map((label, index) => {
      return { value: String(index), label }
    }),
    hours: numbers(0, 23, true),
    minutes: numbers(0, 59, true),
    daysOfMonth: numbers(1, 31, false),
    runOutcomes: OUTCOME_VALUES.map((value, index) => {
      return { value, label: names.outcomes[index] ?? value }
    }),
  }
}

/**
 * Answers `GET /api/v1/cron-options` the way the panel does: every closed set the scheduled-tasks
 * screens select from, named in the language the request carries.
 *
 * It answers from the request's `Accept-Language`, not from a fixed language, because that header is
 * the whole mechanism by which a language switch reaches these names: a stub that always answered in
 * English would let a screen that never asks again pass a spec about switching language.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubCronOptions = async (page: Page): Promise<void> => {
  await page.route(/\/api\/v1\/cron-options(\?.*)?$/, async (route) => {
    const language = languageOf(route.request().headers()['accept-language'])
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(optionsIn(language)),
    })
  })
}
