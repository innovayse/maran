import { computed, type ComputedRef } from 'vue'
import { useModulesStore } from '../stores/modules'
import type { ModuleAccess, PanelModule } from '../types/module'

/**
 * Builds the module access checks from the catalogue store.
 *
 * Every member is a named `const` with its own JSDoc rather than an anonymous
 * entry in the returned object, and every type it names comes from `src/types/`
 * (rules/vue.md).
 * @returns The {@link ModuleAccess} helpers for components and guards.
 */
export const useModuleAccess = (): ModuleAccess => {
  const store = useModulesStore()

  /** The modules this licence covers, in catalogue order. */
  const enabledModules: ComputedRef<PanelModule[]> = computed(() => {
    return store.modules.filter((module) => {
      return module.isEnabled
    })
  })

  /**
   * The modules the panel has but the licence does not cover. They stay visible
   * rather than disappearing: hiding a product the server can run tells the
   * operator nothing, where a locked entry tells them what they could have
   * (rules/architecture.md).
   */
  const lockedModules: ComputedRef<PanelModule[]> = computed(() => {
    return store.modules.filter((module) => {
      return !module.isEnabled
    })
  })

  /**
   * Reports whether a module may be used.
   * @param name The module's machine name.
   * @returns True when the catalogue lists it as enabled.
   */
  const canUse = (name: string): boolean => {
    return store.isEnabled(name)
  }

  /**
   * Reports whether a module is present but not licensed.
   * @param name The module's machine name.
   * @returns True when the catalogue lists it as locked.
   */
  const isLocked = (name: string): boolean => {
    return store.isLocked(name)
  }

  return { enabledModules, lockedModules, canUse, isLocked }
}
