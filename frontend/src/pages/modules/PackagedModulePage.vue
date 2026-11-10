<script setup lang="ts">
/**
 * The screen of an installed (packaged) module: the panel's own page heading, then the module's
 * isolated frame. One component serves every packaged module's every route; the router gives it the
 * module and the module-relative path of the route it matched (`router/registerPackagedModuleRoutes.ts`).
 *
 * The heading is the page's single `<h1>` and carries the screen's name from the module's catalogue
 * entry, already localized by the backend, so a screen reader and the tab title read the same name
 * the module declared. The module's own content is inside the frame and cannot add a heading to the
 * panel's outline. The frame is keyed by route and frame address: moving to another of the module's
 * screens, or the module being updated, mounts a new frame with a new handshake.
 *
 * Renders a `<section>`, not a `<main>`: the single `<main>` landmark belongs to the layout.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import ModuleFrame from '../../components/modules/ModuleFrame.vue'
import { useModulesStore } from '../../stores/modules'
import { packagedRouteName } from '../../utils/packagedNavigation'
import { routeTitleText } from '../../utils/routeTitleText'
import type { PanelModule } from '../../types/module'

/** Props accepted by {@link PackagedModulePage}, given by the route record. */
const props = defineProps<{
  /** The module's machine name. */
  moduleId: string
  /** The module-relative path of the route this page is mounted for. */
  modulePath: string
}>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const modulesStore = useModulesStore()

/** The module's catalogue entry; `undefined` while the catalogue does not list it (it has just been removed). */
const module: ComputedRef<PanelModule | undefined> = computed(() => {
  return modulesStore.modules.find((candidate): boolean => {
    return candidate.name === props.moduleId
  })
})

/** The module when it can be framed (listed, with a frame address); `null` otherwise. */
const framed: ComputedRef<PanelModule | null> = computed(() => {
  const listed = module.value
  return listed !== undefined && listed.spa !== undefined && listed.spa !== null ? listed : null
})

/** The screen's name, from the route's live title. */
const title: ComputedRef<string> = computed(() => {
  return routeTitleText(route.meta, t)
})

/** What keys the frame: the route and the frame address (which carries the module's version). */
const frameKey: ComputedRef<string> = computed(() => {
  return `${props.modulePath}|${module.value?.spa?.frameUrl ?? ''}`
})

/**
 * Opens another of the module's screens, as the module asked.
 * @param path A module-relative path the bridge already checked is a declared route.
 * @returns Resolves once the navigation settled.
 */
const openScreen = async (path: string): Promise<void> => {
  const declared = module.value?.routes?.find((candidate): boolean => {
    return candidate.path === path
  })
  if (declared !== undefined) {
    await router.push({ name: packagedRouteName(props.moduleId, declared.name) })
  }
}
</script>

<template>
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="title" />
    <ModuleFrame
      v-if="framed !== null"
      :key="frameKey"
      :module="framed"
      :path="modulePath"
      @navigate="openScreen"
    />
  </section>
</template>
