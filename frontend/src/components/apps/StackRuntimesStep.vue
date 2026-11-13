<script setup lang="ts">
/**
 * The wizard's second step: choose the runtimes sites will run on, PHP versions and Node.js
 * versions, or none. Optional by design: a static site needs neither, and more can be installed
 * later from the app store. The lists are whatever the host's package sources offer.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import NodejsUnavailableAlert from './NodejsUnavailableAlert.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { HostNodejsUnavailable, InstallableHostApp } from '../../types/hostApp'

/** Props accepted by {@link StackRuntimesStep}. */
defineProps<{
  /** The PHP runtimes the host can install. */
  phpChoices: readonly InstallableHostApp[]
  /** The Node.js runtimes the host can install. */
  nodeChoices: readonly InstallableHostApp[]
  /** The ids of the PHP runtimes chosen so far. */
  phpIds: readonly string[]
  /** The ids of the Node.js runtimes chosen so far. */
  nodeIds: readonly string[]
  /** Why no Node.js runtime is on offer, or `null` when nothing is missing for a reason. */
  nodeUnavailable: HostNodejsUnavailable | null
  /** The panel's sentence for {@link nodeUnavailable}, in the user's language, or `null`. */
  nodeUnavailableMessage: string | null
}>()

/** Events emitted by {@link StackRuntimesStep}. */
defineEmits<{
  /** A runtime was ticked or unticked. */
  (e: 'toggle', family: 'php' | 'nodejs', id: string, chosen: boolean): void
}>()

const { t } = useI18n()
</script>

<template>
  <div class="flex flex-col gap-5">
    <UiAlert v-if="phpChoices.length === 0 && nodeChoices.length === 0" variant="info">
      {{ t('apps.setup.runtimes.none') }}
    </UiAlert>
    <section v-if="phpChoices.length > 0" class="flex flex-col gap-2" :aria-label="t('apps.setup.runtimes.php')">
      <UiSectionHeading :title="t('apps.setup.runtimes.php')" />
      <UiCheckbox
        v-for="app in phpChoices"
        :key="app.appId"
        :model-value="phpIds.includes(app.appId)"
        :label="app.name"
        @update:model-value="$emit('toggle', 'php', app.appId, $event)"
      />
    </section>
    <NodejsUnavailableAlert
      v-if="nodeUnavailable !== null && nodeUnavailableMessage !== null"
      :reason="nodeUnavailable"
      :message="nodeUnavailableMessage"
    />
    <section v-if="nodeChoices.length > 0" class="flex flex-col gap-2" :aria-label="t('apps.setup.runtimes.node')">
      <UiSectionHeading :title="t('apps.setup.runtimes.node')" />
      <UiCheckbox
        v-for="app in nodeChoices"
        :key="app.appId"
        :model-value="nodeIds.includes(app.appId)"
        :label="app.name"
        @update:model-value="$emit('toggle', 'nodejs', app.appId, $event)"
      />
    </section>
    <UiFieldHint>{{ t('apps.setup.runtimes.hint') }}</UiFieldHint>
  </div>
</template>
