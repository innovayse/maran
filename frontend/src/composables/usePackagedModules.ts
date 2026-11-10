import { onBeforeUnmount, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { registerPackagedModuleRoutes } from '../router/registerPackagedModuleRoutes'
import { useModulesStore } from '../stores/modules'

/**
 * Keeps the router and the catalogue in step for installed (packaged) modules. Called once, by the
 * signed-in shell.
 *
 * Two things, both about the catalogue changing underneath a running SPA:
 *
 * - **Routes follow the catalogue.** Whenever the catalogue is replaced (the first load, a language
 *   switch, a refetch) the packaged modules' routes are brought in line with it. The watcher is
 *   synchronous on purpose: the navigation is computed from the catalogue and asks the router
 *   whether a route exists, so the routes must be registered in the same tick the catalogue
 *   arrives, before anything draws a link to them.
 * - **The catalogue is refetched when the window regains focus**, so a module installed, updated or
 *   removed in another tab (or by a restart while the tab sat in the background) appears or goes
 *   without a reload. It asks for `GET /api/v1/modules` only: the catalogue is local, and nothing
 *   else is re-read. A failed refetch keeps the catalogue the shell already has.
 *
 * The store cannot do this itself: one store must not import another, and registering routes is
 * the router's business (rules/vue.md, "State").
 * @returns Nothing; the watcher and the focus listener belong to the calling component.
 */
export const usePackagedModules = (): void => {
  const router = useRouter()
  const modulesStore = useModulesStore()

  /**
   * Re-reads the catalogue when the window regains focus.
   * @returns Nothing.
   */
  const refetch = (): void => {
    // Not before the first load (the layout is already loading it) and not on top of a read in flight.
    if (modulesStore.isLoaded && !modulesStore.loading) {
      void modulesStore.load()
    }
  }

  watch(
    () => {
      return modulesStore.modules
    },
    (modules) => {
      registerPackagedModuleRoutes(router, modules)
    },
    { immediate: true, flush: 'sync' },
  )

  onMounted((): void => {
    window.addEventListener('focus', refetch)
  })

  onBeforeUnmount((): void => {
    window.removeEventListener('focus', refetch)
  })
}
