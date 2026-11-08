import type { Page } from '@playwright/test'
import type { BackupChoice, BackupOptions } from '../../src/types/backupOptions'

/** The languages the panel ships, which is what the stub names its lists in. */
type Language = 'en' | 'ru' | 'hy'

/** What one language calls each named list; the numeric lists need no words. */
interface NamedLists {
  /** Status names: running, completed, failed. */
  statuses: readonly [string, string, string]
  /** Reason names: manual, scheduled, before deletion, before restore. */
  kinds: readonly [string, string, string, string]
  /** Kind-of-storage names: this server's disk, S3-compatible storage. */
  destinationKinds: readonly [string, string]
  /** Cadence names: daily, weekly. */
  frequencies: readonly [string, string]
  /** Weekday names, Monday first. */
  weekdays: readonly [string, string, string, string, string, string, string]
}

/** The words each language gives the named lists, as the backend's resource files do. */
const NAMES: Readonly<Record<Language, NamedLists>> = {
  en: {
    statuses: ['Running', 'Completed', 'Failed'],
    kinds: ['Manual', 'Scheduled', 'Before deletion', 'Before restore'],
    destinationKinds: ["This server's disk", 'S3-compatible storage'],
    frequencies: ['Every day', 'Every week'],
    weekdays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'],
  },
  ru: {
    statuses: ['Выполняется', 'Завершена', 'Не удалась'],
    kinds: ['Вручную', 'По расписанию', 'Перед удалением', 'Перед восстановлением'],
    destinationKinds: ['Диск этого сервера', 'S3-совместимое хранилище'],
    frequencies: ['Каждый день', 'Каждую неделю'],
    weekdays: ['понедельник', 'вторник', 'среда', 'четверг', 'пятница', 'суббота', 'воскресенье'],
  },
  hy: {
    statuses: ['Կատարվում է', 'Ավարտված', 'Ձախողված'],
    kinds: ['Ձեռքով', 'Ըստ ժամանակացույցի', 'Ջնջումից առաջ', 'Վերականգնումից առաջ'],
    destinationKinds: ['Այս սերվերի սկավառակը', 'S3-համատեղելի պահոց'],
    frequencies: ['Ամեն օր', 'Ամեն շաբաթ'],
    weekdays: ['երկուշաբթի', 'երեքշաբթի', 'չորեքշաբթի', 'հինգշաբթի', 'ուրբաթ', 'շաբաթ', 'կիրակի'],
  },
}

/** The values the panel sends for each named list, in the same order as the words. */
const VALUES = {
  statuses: ['running', 'completed', 'failed'],
  kinds: ['manual', 'scheduled', 'preDeletion', 'preRestore'],
  destinationKinds: ['local', 's3'],
  frequencies: ['daily', 'weekly'],
  weekdays: ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'],
} as const

/**
 * Pairs a list's values with the words a language gives them.
 * @param values The values the panel sends.
 * @param labels The words, in the same order.
 * @returns One choice per value.
 */
const named = (values: readonly string[], labels: readonly string[]): BackupChoice[] => {
  return values.map((value, index) => {
    return { value, label: labels[index] ?? value }
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
const optionsIn = (language: Language): BackupOptions => {
  const names = NAMES[language]
  return {
    statuses: named(VALUES.statuses, names.statuses),
    kinds: named(VALUES.kinds, names.kinds),
    destinationKinds: named(VALUES.destinationKinds, names.destinationKinds),
    frequencies: named(VALUES.frequencies, names.frequencies),
    weekdays: named(VALUES.weekdays, names.weekdays),
    hours: Array.from({ length: 24 }, (_, hour) => {
      return { value: String(hour), label: `${String(hour).padStart(2, '0')}:00` }
    }),
    retainCounts: Array.from({ length: 365 }, (_, index) => {
      return { value: String(index + 1), label: String(index + 1) }
    }),
    // A customer's own schedule: the same two cadences, and copies up to the customer ceiling.
    customerFrequencies: named(VALUES.frequencies, names.frequencies),
    customerRetainCounts: Array.from({ length: 7 }, (_, index) => {
      return { value: String(index + 1), label: String(index + 1) }
    }),
  }
}

/**
 * Answers `GET /api/v1/backup-options` the way the panel does: every closed set the backup screens
 * select from, named in the language the request carries.
 *
 * It answers from the request's `Accept-Language`, not from a fixed language, because that header is
 * the whole mechanism by which a language switch reaches these names: a stub that always answered in
 * English would let a screen that never asks again pass a spec about switching language.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubBackupOptions = async (page: Page): Promise<void> => {
  await page.route(/\/api\/v1\/backup-options(\?.*)?$/, async (route) => {
    const language = languageOf(route.request().headers()['accept-language'])
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(optionsIn(language)),
    })
  })
}
