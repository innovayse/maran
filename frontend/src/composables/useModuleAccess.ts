import { computed } from 'vue'
import { useModulesStore } from '../stores/modules'
import type { ModuleAccess } from '../types/module'

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
