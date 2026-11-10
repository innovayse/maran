import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsTemplatesApi } from '../composables/apis/useDnsTemplatesApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type {
  DnsTemplate,
  DnsTemplateChoice,
  DnsTemplateDetail,
  DnsTemplateLoad,
  DnsTemplateVariable,
  SaveDnsTemplateRequest,
} from '../types/dnsTemplate'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the page of the templates list on screen, one template's detail, the variable catalogue,
 * the template choices of the create-zone form and the template mutations. The DNS pages read
 * state from here and call its actions; none touches the API composable directly (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useDnsTemplatesStore = defineStore('dnsTemplates', () => {
  const api = useDnsTemplatesApi()
  const latestRead = useLatestRead()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const templates: Ref<DnsTemplate[]> = ref([])

  /** What the form page last learned about the template it asked for. */
  const detail: Ref<DnsTemplateLoad> = ref({ state: 'loading' })

  /** The variable catalogue, localized by the panel; empty until it has answered. */
  const variables: Ref<DnsTemplateVariable[]> = ref([])

  /** The templates a new zone can start from; empty until the panel has answered. */
  const choices: Ref<DnsTemplateChoice[]> = ref([])

  /** Whether the choices have been answered (even with none), so a form knows when to preselect. */
  const choicesLoaded: Ref<boolean> = ref(false)

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True while a delete or a make-default is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** The panel's reason the last save failed, already localized, or `null`. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** The panel's reason the last delete or make-default failed, already localized, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the template list into {@link templates} and reports how the read ended,
   * for the page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<DnsTemplate>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        templates.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads one template into {@link detail}. A 404 is an answer, not a failure.
   * @param id The template to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    detail.value = { state: 'loading' }
    try {
      detail.value = { state: 'found', template: await api.get(id) }
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        detail.value = { state: 'notFound' }
      } else {
        detail.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Reads the variable catalogue. A failure keeps what is held: the catalogue is a help beside
   * the form, and the panel's refusal on save is what decides.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadVariables = async (): Promise<void> => {
    try {
      variables.value = await api.variables()
    } catch {
      // The help is advisory; a missing list must not stop the form being used.
    }
  }

  /**
   * Reads the templates the create-zone form offers. A failure leaves the list empty, which the
   * form reads as "start from an empty zone".
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadChoices = async (): Promise<void> => {
    try {
      choices.value = await api.choices()
    } catch {
      choices.value = []
    } finally {
      choicesLoaded.value = true
    }
  }

  /**
   * Creates a template, or replaces one when an id is given.
   * @param id The template to replace, or `null` to create a new one.
   * @param request The name, description and records.
   * @returns The saved template, or `null` when the request failed; the caller reads
   * {@link saveErrorMessage} for the reason.
   */
  const save = async (id: string | null, request: SaveDnsTemplateRequest): Promise<DnsTemplateDetail | null> => {
    saving.value = true
    try {
      const saved = id === null ? await api.create(request) : await api.update(id, request)
      saveErrorMessage.value = null
      return saved
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Deletes a template.
   * @param id The template to delete.
   * @returns True when the panel deleted it; otherwise {@link actionErrorMessage} holds the reason.
   */
  const remove = async (id: string): Promise<boolean> => {
    return act(() => {
      return api.remove(id)
    })
  }

  /**
   * Makes a template the default of zones created from a site.
   * @param id The template.
   * @returns True when the panel changed the default; otherwise {@link actionErrorMessage} holds the reason.
   */
  const makeDefault = async (id: string): Promise<boolean> => {
    return act(() => {
      return api.makeDefault(id)
    })
  }

  /**
   * Runs one row action and keeps the panel's refusal.
   * @param run The request to send.
   * @returns True when it succeeded.
   */
  const act = async (run: () => Promise<void>): Promise<boolean> => {
    acting.value = true
    try {
      await run()
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Forgets the last refusals, so a screen that opens does not greet the person with a reason that
   * belonged to an earlier visit.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    saveErrorMessage.value = null
    actionErrorMessage.value = null
  }

  return {
    templates,
    detail,
    variables,
    choices,
    choicesLoaded,
    saving,
    acting,
    saveErrorMessage,
    actionErrorMessage,
    list,
    loadOne,
    loadVariables,
    loadChoices,
    save,
    remove,
    makeDefault,
    clearErrors,
  }
})
