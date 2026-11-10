import { expect, test } from '../fixtures/real-test'
import { chooseLanguage, signInAs } from '../fixtures/real-sign-in'
import { openDestination, sidebarDestinations } from '../fixtures/real-sidebar-walk'
import { findUntranslatedText, type TranslatedLocale } from '../fixtures/untranslated-text'

// Every page the administrator's sidebar offers, in Russian and in Armenian, with no English chrome
// and no raw i18n key left on any of them (issue #519; the audit found stale English banners and
// raw `key=value` text only by reading thirty-five pages in each language).

/** The two languages, each with the name its own menu entry shows. */
const LANGUAGES: { locale: TranslatedLocale; menuName: string }[] = [
  { locale: 'ru', menuName: 'Русский' },
  { locale: 'hy', menuName: 'Հայերեն' },
]

for (const { locale, menuName } of LANGUAGES) {
  test(`every administrator page reads in ${locale} with no English chrome and no raw key`, async ({ page }) => {
    test.setTimeout(300_000)
    await signInAs(page, 'administrator')
    await chooseLanguage(page, menuName)

    // The sidebar is read AFTER the switch: its link names are the translated ones, and clicking them
    // is the same walk a person in that language makes.
    const destinations = await sidebarDestinations(page)
    expect(destinations.length, 'the sidebar offered almost nothing to walk').toBeGreaterThan(10)

    const findings: string[] = []
    for (const destination of destinations) {
      await openDestination(page, destination)
      findings.push(...(await findUntranslatedText(page, locale)).map((finding) => {return `${destination}: ${finding}`}))
    }

    expect(findings).toEqual([])
  })
}
