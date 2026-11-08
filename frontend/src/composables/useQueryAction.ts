import { nextTick, watch, type Ref } from 'vue'
import { useRoute, useRouter, type LocationQueryValue } from 'vue-router'
import { QUICK_ACTION_CREATE, QUICK_ACTION_PARAMETER } from '../router/quickActions'
import { focusableElements } from '../utils/focusableElements'

/**
 * Lets a screen answer a dashboard shortcut (`?action=create`) by bringing its own create form into
 * view: scrolls to the form and focuses its first field, then removes the parameter from the address
 * so a reload or a Back does not do it again.
 *
 * This is the whole of the "quick action" contract on the receiving side. The shortcut is a link to
 * the screen that owns the work; the screen shows its own form, so there is one form, one validation
 * path and no dialog duplicated onto the dashboard. A screen with no such parameter in its address
 * is unaffected, and a parameter with any other value is left alone.
 *
 * The form may not exist yet when the screen mounts (the screen is still loading), so the target is
 * watched: the action runs once, the first time the form is present.
 * @param target The form: a template ref to a component instance or an element, `null` until it is drawn.
 * @returns Nothing.
 */
export const useQueryAction = (target: Readonly<Ref<object | null>>): void => {
  const route = useRoute()
  const router = useRouter()

  /** Whether the current ask has been answered, so the form appearing again later does not repeat it. */
  let answered = false

  watch(
    [
      target,
      (): LocationQueryValue | LocationQueryValue[] | undefined => {
        return route.query[QUICK_ACTION_PARAMETER]
      },
    ],
    async ([form, requested]) => {
      if (requested !== QUICK_ACTION_CREATE) {
        // The ask is gone (answered, or never made): the screen can be asked again, for a shortcut
        // followed while the screen is already open.
        answered = false
        return
      }
      if (answered || form === null) {
        return
      }
      answered = true

      await nextTick()
      // A component instance exposes its root element as `$el`; an element is its own root.
      const root: unknown = form instanceof HTMLElement ? form : (form as { $el?: unknown }).$el
      if (root instanceof HTMLElement) {
        root.scrollIntoView({ block: 'start' })
        focusableElements(root)[0]?.focus()
      }

      // The address no longer asks for the form; `replace` so Back does not return to the ask.
      const rest = Object.fromEntries(
        Object.entries(route.query).filter(([name]) => {
          return name !== QUICK_ACTION_PARAMETER
        }),
      )
      await router.replace({ query: rest })
    },
    { immediate: true, flush: 'post' },
  )
}
