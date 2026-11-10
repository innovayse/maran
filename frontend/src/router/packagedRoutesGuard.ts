import type { NavigationGuard, RouteLocationRaw, Router } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { registerPackagedModuleRoutes } from './registerPackagedModuleRoutes'

/** The prefix every packaged module's screens live under. */
const PACKAGED_PREFIX = '/m/'

/**
 * Global navigation guard that makes a packaged module's screen reachable by its address.
 *
 * The routes of installed modules are added while the SPA runs, after the catalogue is loaded. A
 * deep link or a reload on `/m/<id>/…` is resolved BEFORE that, and would land on the not-found
 * screen of a module that is installed and running. So when a navigation under `/m/` resolved to
 * not-found, this guard loads the catalogue (if it is not loaded yet), registers the routes, and
 * asks for the same address again. An address that still resolves to nothing stays not-found: the
 * module is not installed, not running, or does not declare that screen.
 *
 * Registered after the sign-in guard, so the catalogue is only asked for by a signed-in person.
 * @param router The application router, which the routes are added to.
 * @returns A navigation guard to register with `router.beforeEach`.
 */
export const createPackagedRoutesGuard = (router: Router): NavigationGuard => {
  return async (to): Promise<boolean | RouteLocationRaw> => {
    if (to.name !== 'not-found' || !to.path.startsWith(PACKAGED_PREFIX)) {
      return true
    }
    const modulesStore = useModulesStore()
    if (!modulesStore.isLoaded) {
      await modulesStore.load()
    }
    registerPackagedModuleRoutes(router, modulesStore.modules)
    // Only a retry when the address resolves to a real screen now: otherwise the redirect would be
    // answered by this guard again, and again.
    if (router.resolve(to.fullPath).name === 'not-found') {
      return true
    }
    return { path: to.path, query: to.query, hash: to.hash, replace: true }
  }
}
