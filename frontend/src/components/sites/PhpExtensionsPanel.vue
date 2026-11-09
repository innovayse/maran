<script setup lang="ts">
/**
 * The extensions of one installed PHP version, each with a switch for an administrator and a plain
 * on/off word for anyone else.
 *
 * **Who may change what is the server's answer, not this component's guess.** An administrator's
 * list carries `availability` on every row; a customer's carries `null`. The panel therefore
 * renders switches exactly where `availability` is present and a read-only state word where it is
 * not. This is advice for the eye: the backend refuses the change by role on every request
 * (rules/security.md), so a row that errs permissive costs a refused request, never a change.
 *
 * **A switch that cannot move says why, in text.** An extension no package repository offers
 * (`notPackaged`) and one that is on but cannot be switched off on this system
 * (`disableNotSupported`) render a disabled switch with a plain sentence beneath it. A disabled
 * control with no reason is a dead end for a keyboard or screen-reader user, who cannot hover for
 * a tooltip.
 *
 * **One change at a time, and the state shown is the state read back.** A change reloads PHP for
 * every account of the version, and installing the extension's package first can take minutes, so
 * while one is applying every switch is held and a notice says so with a way to the tasks screen,
 * where the panel records its progress. The store re-reads the list afterwards; this component
 * never flips a switch on the strength of having been clicked.
 *
 * **Every label, and every refusal, is the server's.** Extension labels and the text of a failed
 * change arrive already in the operator's language and are printed as received.
 *
 * The component loads its own list through the store (the PHP versions page and the site's PHP tab
 * both place it with a version and nothing else) and reloads when the version changes.
 */
import { computed, onMounted, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import { usePhpVersionsStore } from '../../stores/phpVersions'
import type { PhpExtension } from '../../types/phpVersion'

/** Props accepted by {@link PhpExtensionsPanel}. */
const props = defineProps<{
  /** The installed version whose extensions are listed, e.g. `8.3`. */
  version: string
}>()

/** One extension as the list draws it: the server's row and why its switch cannot move, if it cannot. */
interface ExtensionRow {
  /** The extension as the server described it. */
  extension: PhpExtension
  /** The locale leaf under `phpExtensions.reason` that explains a held switch, or `null` when it can move. */
  reason: 'notPackaged' | 'disableNotSupported' | 'unknown' | null
}

/** How many placeholder rows stand in for the list while it loads. */
const PLACEHOLDER_ROWS = 4

const { t } = useI18n()
const phpVersionsStore = usePhpVersionsStore()

/** The placeholder row indexes, for the loading state's `v-for`. */
const placeholders: number[] = Array.from({ length: PLACEHOLDER_ROWS }, (_unused, index) => {
  return index
})

/** The rows to draw, each with the reason its switch is held (if it is). */
const rows: ComputedRef<ExtensionRow[]> = computed(() => {
  return phpVersionsStore.extensions.map((extension): ExtensionRow => {
    switch (extension.availability) {
      case 'notPackaged':
        return { extension, reason: 'notPackaged' }
      case 'disableNotSupported':
        // Switching ON is possible (by installing the package); only OFF is not.
        return { extension, reason: extension.enabled ? 'disableNotSupported' : null }
      case 'unknown':
        return { extension, reason: 'unknown' }
      default:
        return { extension, reason: null }
    }
  })
})

/** The label of the extension a change is applying to, or `null` when none is. */
const switchingLabel: ComputedRef<string | null> = computed(() => {
  const name = phpVersionsStore.switchingExtension
  if (name === null) {
    return null
  }
  return (
    phpVersionsStore.extensions.find((candidate) => {
      return candidate.name === name
    })?.label ?? name
  )
})

/**
 * Asks the store to switch one extension of this version.
 * @param name The extension's catalogue name.
 * @param enabled The state the switch was moved to.
 * @returns Resolves once the change has settled and the list has been re-read.
 */
const change = async (name: string, enabled: boolean): Promise<void> => {
  await phpVersionsStore.setExtension(props.version, name, enabled)
}

onMounted(async () => {
  await phpVersionsStore.loadExtensions(props.version)
})

watch(
  () => {return props.version},
  async (version) => {
    await phpVersionsStore.loadExtensions(version)
  },
)
</script>

<template>
  <div class="flex flex-col gap-3">
    <UiLoadingRegion :state="phpVersionsStore.extensionsState">
      <!-- The server's own refusal or failure to list, in its words. -->
      <template #error>
        <UiAlert variant="error">{{ phpVersionsStore.extensionsErrorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <ul v-if="phpVersionsStore.extensionsState === 'loading'" class="flex flex-col gap-3">
        <li v-for="placeholder in placeholders" :key="placeholder">
          <UiSkeleton shape="line" />
        </li>
      </ul>

      <UiEmptyState
        v-else-if="rows.length === 0"
        :title="t('phpExtensions.empty.title')"
        :description="t('phpExtensions.empty.description')"
      />

      <ul v-else class="flex flex-col gap-3">
        <li v-for="row in rows" :key="row.extension.name" class="flex flex-col gap-1">
          <!-- availability is null for a caller who may not change anything: words, no switches. -->
          <div v-if="row.extension.availability === null" class="flex items-center justify-between gap-3">
            <span class="min-w-0 text-sm text-text-primary wrap-anywhere">{{ row.extension.label }}</span>
            <UiBadge :variant="row.extension.enabled ? 'success' : 'neutral'">
              {{ row.extension.enabled ? t('phpExtensions.state.on') : t('phpExtensions.state.off') }}
            </UiBadge>
          </div>
          <template v-else>
            <UiSwitch
              :model-value="row.extension.enabled"
              :label="row.extension.label"
              :disabled="row.reason !== null || phpVersionsStore.switchingExtension !== null"
              @update:model-value="change(row.extension.name, $event)"
            />
            <p v-if="row.reason !== null" class="text-sm text-text-muted">
              {{ t(`phpExtensions.reason.${row.reason}`) }}
            </p>
          </template>
        </li>
      </ul>
    </UiLoadingRegion>

    <UiAlert v-if="switchingLabel !== null" variant="info">
      {{ t('phpExtensions.applying', { extension: switchingLabel, version: props.version }) }}
      <template #actions>
        <UiButton variant="secondary" to="/tasks">{{ t('phpExtensions.openTasks') }}</UiButton>
      </template>
    </UiAlert>

    <UiAlert v-if="phpVersionsStore.extensionSwitchErrorMessage" variant="error">
      {{ phpVersionsStore.extensionSwitchErrorMessage }}
    </UiAlert>
  </div>
</template>
