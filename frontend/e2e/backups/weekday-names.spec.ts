import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupSchedules } from '../fixtures/stub-backup-schedule-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import type { Account } from '../../src/types/account'
import type { BackupSchedule } from '../../src/types/backupSchedule'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** A weekly schedule, so the screen actually draws the weekday picker. */
const WEEKLY: BackupSchedule = {
  id: '55555555-5555-5555-5555-555555555555',
  accountId: null,
  destinationId: null,
  frequency: 'weekly',
  hourUtc: 2,
  dayOfWeekUtc: 'tuesday',
  retainCount: 4,
  enabled: true,
  lastRunAt: '2026-09-01T02:00:00Z',
}

/**
 * Chooses a language from the header's locale menu.
 *
 * Located by ROLE rather than by the trigger's accessible name, which is itself translated: a
 * helper naming it in English stops finding it the moment the page has switched to Russian.
 * @param page The page under test.
 * @param language The language's own name, as the option renders it.
 * @returns Resolves once the option has been chosen.
 */
const chooseLanguage = async (page: Page, language: string): Promise<void> => {
  await page.getByRole('banner').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: language, exact: true }).click()
}

/**
 * Opens the weekday picker, whose label is given in the language the page is currently in.
 * @param page The page under test.
 * @param dayLabel The picker's label as it currently reads.
 * @returns Resolves once the option list is open.
 */
const openDayPicker = async (page: Page, dayLabel: string): Promise<void> => {
  await page.getByRole('combobox', { name: dayLabel }).click()
}

/**
 * Puts the backup schedule screen on the page with a weekly schedule already saved.
 * @param page The page under test.
 * @returns Resolves once the screen has been navigated to.
 */
const openSchedule = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackupSchedules(page, { host: WEEKLY })
  await page.goto('/backups/schedule')
}

// The names come from date-fns, resolved per render against the locale store — not from a list
// this panel translates, and not from one computed once at module load. A frozen list is the
// failure this spec exists for, and it is invisible to any assertion about how MANY days there
// are: it would still be seven, still in order, still in the language the page happened to open
// in. So the assertions name the actual words on both sides of the switch.
test('the weekday names follow a language switch, in each language own words', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openSchedule(page)

  // Positive control: the screen is on the page and this probe can read the picker, so a later
  // silence is a fact about the names rather than about a spec looking at the wrong screen.
  await expect(page.getByRole('combobox', { name: 'Day of the week' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Day of the week' })).toHaveText('Tuesday')

  await openDayPicker(page, 'Day of the week')
  await expect(page.getByRole('option')).toHaveText([
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ])
  await page.keyboard.press('Escape')

  await chooseLanguage(page, 'Русский')

  // The whole point: the same picker, no reload, now reading the other language's words.
  await expect(page.getByRole('combobox', { name: 'День недели' })).toHaveText('вторник')

  await openDayPicker(page, 'День недели')
  // Lower case, and exactly so: Russian does not capitalise weekday names. Asserting the strings
  // the library returns is what keeps this panel from re-inventing the ones it deleted, which
  // were capitalised and therefore wrong beside every date the same library renders.
  await expect(page.getByRole('option')).toHaveText([
    'понедельник',
    'вторник',
    'среда',
    'четверг',
    'пятница',
    'суббота',
    'воскресенье',
  ])
  await page.keyboard.press('Escape')

  await chooseLanguage(page, 'Հայերեն')

  await expect(page.getByRole('combobox', { name: 'Շաբաթվա օրը' })).toHaveText('երեքշաբթի')
})
