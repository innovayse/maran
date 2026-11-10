import type { Component } from 'vue'
import type { RouteRecordRaw, Router } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { packagedEntryKey, packagedLayoutName, packagedRouteName } from '../utils/packagedNavigation'
import type { ModuleRoute, PanelModule } from '../types/module'

/**
 * Registers the screens of installed (packaged) modules with the router while the SPA is running,
 * so a module installed after this bundle was built has routes without a rebuild.
 *
 * Each running packaged module that has a frame (`spa`) gets ONE layout record at `/m/<id>`, named
 * `module-<id>`, with a child per route the module declares at `/m/<id><path>`, named
 * `module-<id>-<routeName>`, all rendering {@link PackagedModulePage}. The routes carry
 * `meta.module = <id>`, so the licence guard that gates every compiled-in module gates these too:
 * a locked module's screens resolve to the upgrade page, not to a frame. A module the catalogue no
 * longer lists (or lists as anything but running, or without a frame) has its record removed.
 *
 * It is called after every catalogue load. A module whose routes did not change is left alone, so a
 * refetch on window focus does not churn the router.
 */

/** A module id the router may be given: letters, digits, dash and underscore. */
const MODULE_ID_PATTERN = /^[A-Za-z0-9_-]+$/

/** A module-relative route path the router may be given: no parameters, no pattern syntax. */
const MODULE_PATH_PATTERN = /^\/[A-Za-z0-9._~/-]*$/

/** A route name within a module the router may be given. */
const ROUTE_NAME_PATTERN = /^[A-Za-z0-9_-]+$/

/**
 * Answers whether a catalogue entry is an installed module with a screen to register.
 * @param module A catalogue entry.
 * @returns True when it is packaged, running and framed.
 */
const hasFrame = (module: PanelModule): boolean => {
  return (
    module.isPackaged === true &&
    (module.lifecycle ?? 'active') === 'active' &&
    module.spa !== undefined &&
    module.spa !== null &&
    MODULE_ID_PATTERN.test(module.name)
  )
}

/**
 * The routes of a module the router can be given. A route the backend declared with a path or name
 * outside the closed pattern is skipped rather than interpreted: a path is data from a package, and
 * vue-router would read `:x(.*)` in it as syntax.
 * @param module A packaged module.
 * @returns The usable routes.
 */
const usableRoutes = (module: PanelModule): ModuleRoute[] => {
  return (module.routes ?? []).filter((route): boolean => {
    return MODULE_PATH_PATTERN.test(route.path) && ROUTE_NAME_PATTERN.test(route.name)
  })
}

/**
 * What a module's registered routes were built from; two equal fingerprints mean nothing in the
 * router needs to change.
 * @param module A packaged module.
 * @returns The fingerprint.
 */
const signatureOf = (module: PanelModule): string => {
  return JSON.stringify({
    version: module.version ?? '',
    frame: module.spa?.frameUrl ?? '',
    routes: usableRoutes(module).map((route): string[] => {
      return [route.name, route.path]
    }),
    menu: (module.menu ?? []).map((item): string[] => {
      return [item.key, item.routeName]
    }),
  })
}

/**
 * The text of a module screen's name, read from the live catalogue so a language switch (which
 * re-reads it) renames the screen on display.
 * @param moduleId The module's machine name.
 * @param routeName The route's name within the module.
 * @returns A function giving the localized title, empty when the catalogue no longer has it.
 */
const titleFrom = (moduleId: string, routeName: string): (() => string) => {
  return (): string => {
    const module = useModulesStore().modules.find((candidate): boolean => {
      return candidate.name === moduleId
    })
    return module?.routes?.find((route): boolean => {
      return route.name === routeName
    })?.title ?? ''
  }
}

/**
 * Builds the layout record of one packaged module.
 * @param module A packaged module that passed {@link hasFrame}.
 * @returns The record to add to the router.
 */
const recordOf = (module: PanelModule): RouteRecordRaw => {
  const id = module.name
  const entryOf = new Map(
    (module.menu ?? []).map((item): [string, string] => {
      return [item.routeName, packagedEntryKey(id, item.key)]
    }),
  )
  return {
    path: `/m/${id}`,
    name: packagedLayoutName(id),
    // Imported on use: the layout mounts this module's wiring, so importing it here would be a cycle.
    component: (): Promise<{ default: Component }> => {
      return import('../layouts/DefaultLayout.vue')
    },
    meta: { packaged: id, packagedSignature: signatureOf(module) },
    children: usableRoutes(module).map((route): RouteRecordRaw => {
      return {
        // Relative to `/m/<id>`: the module path without its leading slash (`/` becomes the empty path).
        path: route.path.slice(1),
        name: packagedRouteName(id, route.name),
        component: (): Promise<{ default: Component }> => {
          return import('../pages/modules/PackagedModulePage.vue')
        },
        props: { moduleId: id, modulePath: route.path },
        meta: { module: id, titleFrom: titleFrom(id, route.name), packagedEntry: entryOf.get(route.name) },
      }
    }),
  }
}

/**
 * Brings the router's packaged-module routes in line with the catalogue.
 * @param router The application router.
 * @param modules The catalogue as last loaded.
 * @returns Nothing; routes are added, replaced or removed on the router.
 */
export const registerPackagedModuleRoutes = (router: Router, modules: readonly PanelModule[]): void => {
  const wanted = new Map(
    modules.filter(hasFrame).map((module): [string, PanelModule] => {
      return [module.name, module]
    }),
  )
  const registered = new Map(
    router
      .getRoutes()
      .filter((route): boolean => {
        return route.meta.packaged !== undefined && route.name === packagedLayoutName(route.meta.packaged)
      })
      .map((route): [string, string] => {
        return [route.meta.packaged ?? '', route.meta.packagedSignature ?? '']
      }),
  )
  for (const id of registered.keys()) {
    if (!wanted.has(id)) {
      router.removeRoute(packagedLayoutName(id))
    }
  }
  for (const [id, module] of wanted) {
    if (registered.get(id) === signatureOf(module)) {
      continue
    }
    // Replace in one step: a record of the same name is removed first so its children go with it.
    if (registered.has(id)) {
      router.removeRoute(packagedLayoutName(id))
    }
    router.addRoute(recordOf(module))
  }
}
