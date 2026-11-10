<script setup lang="ts">
/**
 * What a module declares about itself, as a description list: publisher, licence tier, the oldest
 * panel it runs on, the database schema it owns, the modules it needs, the parts of the server's
 * root agent it reaches and the permissions it declares. The data is the panel's copy of the signed
 * manifest, localized by the panel and printed as plain text; nothing in it comes from the package's
 * own bundle. The install dialog shows it before an administrator confirms, and the detail page
 * shows it beside the versions.
 */
import { useI18n } from 'vue-i18n'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import type { ModuleManifestSummary } from '../../types/marketplace'

/** Props accepted by {@link ModuleManifestList}. */
defineProps<{
  /** The panel's summary of the manifest. */
  manifest: ModuleManifestSummary
}>()

const { t } = useI18n()
</script>

<template>
  <UiDescriptionList data-testid="module-manifest">
    <UiDescriptionItem :term="t('marketplace.manifest.publisher')">{{ manifest.publisher }}</UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.tier')">{{ manifest.tierDisplayName }}</UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.minPanel')" mono>{{ manifest.minPanelVersion }}</UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.schema')" mono>
      {{ manifest.schemaName ?? t('marketplace.manifest.noSchema') }}
    </UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.dependencies')" mono>
      {{ manifest.dependencies.length === 0 ? t('marketplace.manifest.none') : manifest.dependencies.join(', ') }}
    </UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.agentCapabilities')">
      <template v-if="manifest.agentCapabilities.length === 0">{{ t('marketplace.manifest.none') }}</template>
      <ul v-else class="list-disc space-y-0.5 pl-5">
        <li v-for="capability in manifest.agentCapabilities" :key="capability.key">{{ capability.displayName }}</li>
      </ul>
    </UiDescriptionItem>
    <UiDescriptionItem :term="t('marketplace.manifest.permissions')">
      <template v-if="manifest.permissions.length === 0">{{ t('marketplace.manifest.none') }}</template>
      <ul v-else class="list-disc space-y-0.5 pl-5">
        <li v-for="permission in manifest.permissions" :key="permission.key">{{ permission.displayName }}</li>
      </ul>
    </UiDescriptionItem>
  </UiDescriptionList>
</template>
