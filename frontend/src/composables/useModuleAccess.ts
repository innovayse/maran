import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useModulesStore } from '../stores/modules'
import type { PanelModule } from '../types/module'

/**
 * Module availability checks for the interface: what to render, what to lock behind an upgrade
 * prompt, and what the router guard should let through.
 *
 * These checks are cosmetic. The backend enforces the licence on every request, so this composable
 * decides what a user *sees*, never what they *may do* (rules/architecture.md).
 */
export interface ModuleAccess {
  /** Modules the licence permits, in the order the panel reported them. */
  enabledModules: ComputedRef<PanelModule[]>
  /** Modules the panel knows but the licence does not permit — candidates for an upgrade prompt. */
  lockedModules: ComputedRef<PanelModule[]>
  /**
   * Whether a module may be used.
   * @param name Machine name of the module.
   * @returns True when the licence permits it.
   */
  canUse: (name: string) => boolean
  /**
   * Whether a module exists but is licence-locked.
   * @param name Machine name of the module.
   * @returns True when it should render as locked rather than absent.
   */
  isLocked: (name: string) => boolean
}

/**
 * Builds the module access checks from the catalogue store.
 * @returns The {@link ModuleAccess} helpers for components and guards.
 */
export const useModuleAccess = (): ModuleAccess => {
  const store = useModulesStore()

  const enabledModules = computed(() => store.modules.filter((module) => module.isEnabled))
  const lockedModules = computed(() => store.modules.filter((module) => !module.isEnabled))

  return {
    enabledModules,
    lockedModules,
    canUse: (name: string): boolean => store.isEnabled(name),
    isLocked: (name: string): boolean => store.isLocked(name),
  }
}
