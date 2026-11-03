import { onMounted, watch } from 'vue'
import { useLocaleStore } from '../stores/locale'

/**
 * Runs a read when the screen opens and again whenever the interface language changes.
 *
 * For the choice lists a store holds (frequencies, weekdays, statuses…): the backend names them in
 * the language the request carries, so a list read in English is wrong the moment the reader
 * switches to Russian, and the only honest repair is to ask again. `useApi` sends the current
 * language with every request, so the repeat read returns the new names.
 * @param load The read to run, normally a store action.
 * @returns Nothing; the hooks are registered on the calling component.
 */
export const useReloadOnLocale = (load: () => Promise<void>): void => {
  const localeStore = useLocaleStore()

  onMounted(load)

  watch(
    () => {
      return localeStore.current
    },
    () => {
      void load()
    },
  )
}
