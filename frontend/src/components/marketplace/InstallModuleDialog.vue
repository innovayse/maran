<script setup lang="ts">
/**
 * The install (and update) acknowledgement: what a module declares it can reach, shown BEFORE the
 * administrator confirms with their password. An installed module runs inside the panel process, so
 * this is the most privileged thing the panel's interface offers (rules/security.md item 13) and the
 * dialog does not let it pass quickly.
 *
 * Every line about the module comes from the panel's own copy of the signed manifest
 * (`manifest`), localized by the panel, and is printed as plain text; nothing here is read from the
 * package. Without a manifest summary there is nothing to acknowledge, so the dialog says so and
 * cannot be submitted. Submitting needs the acknowledgement ticked and the password typed (the
 * password field and its rules are {@link PasswordConfirmDialog}'s); the panel re-checks both.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import ModuleManifestList from './ModuleManifestList.vue'
import PasswordConfirmDialog from './PasswordConfirmDialog.vue'
import type { InstallModuleRequest, ModuleManifestSummary } from '../../types/marketplace'

/** Props accepted by {@link InstallModuleDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the caller. */
  open: boolean
  /** `install` for a module not on the panel, `update` for a newer version of one that is. */
  mode: 'install' | 'update'
  /** The module's name, localized by the panel. */
  moduleName: string
  /** The version that would be installed. */
  version: string
  /** The panel's summary of the version's manifest, or `null` when none is held. */
  manifest: ModuleManifestSummary | null
  /** True while the manifest summary is being read. */
  manifestLoading: boolean
  /** Whether the request is in flight. */
  acting: boolean
  /** The panel's sentence for the last refusal, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link InstallModuleDialog}. */
const emit = defineEmits<{
  /** The administrator declined. */
  (e: 'close'): void
  /** The administrator confirmed; carries the request body. */
  (e: 'submit', request: InstallModuleRequest): void
}>()

const { t } = useI18n()

/** Whether the administrator ticked the acknowledgement; cleared whenever the dialog opens. */
const acknowledged: Ref<boolean> = ref(false)

/** The dialog's heading. */
const title: ComputedRef<string> = computed(() => {
  return t(props.mode === 'install' ? 'marketplace.install.title' : 'marketplace.update.title', { name: props.moduleName })
})

/** The acknowledgement sentence for the mode. */
const acknowledgement: ComputedRef<string> = computed(() => {
  return t(props.mode === 'install' ? 'marketplace.install.acknowledge' : 'marketplace.update.acknowledge')
})

/** Whether the submit button may be enabled apart from the password: a manifest to read and the box ticked. */
const canSubmit: ComputedRef<boolean> = computed(() => {
  return props.manifest !== null && acknowledged.value
})

/**
 * Sends the request body upward once the password was typed.
 * @param password The administrator's current password.
 * @returns Nothing.
 */
const submit = (password: string): void => {
  emit('submit', { version: props.version, acknowledged: acknowledged.value, password })
}

watch(
  (): boolean => {
    return props.open
  },
  (isOpen: boolean): void => {
    if (isOpen) {
      acknowledged.value = false
    }
  },
  { immediate: true },
)
</script>

<template>
  <PasswordConfirmDialog
    :open="open"
    :title="title"
    :submit-label="t(mode === 'install' ? 'marketplace.install.submit' : 'marketplace.update.submit')"
    :acting="acting"
    :acting-label="t('marketplace.switch.working')"
    :error-message="errorMessage"
    :can-submit="canSubmit"
    @close="emit('close')"
    @submit="submit"
  >
    <UiSpinner v-if="manifestLoading" :label="t('common.loading.busy')" />
    <template v-else>
      <UiAlert variant="warning">{{ t('marketplace.install.warning') }}</UiAlert>

      <UiAlert v-if="manifest === null" variant="info" data-testid="install-manifest-missing">
        {{ t('marketplace.install.manifestMissing') }}
      </UiAlert>
      <template v-else>
        <UiDescriptionList>
          <UiDescriptionItem
            :term="t(mode === 'install' ? 'marketplace.install.versionLabel' : 'marketplace.update.versionLabel')"
            mono
          >
            {{ version }}
          </UiDescriptionItem>
        </UiDescriptionList>
        <ModuleManifestList :manifest="manifest" />
      </template>

      <UiCheckbox v-model="acknowledged" :label="acknowledgement" :disabled="manifest === null" />
    </template>
  </PasswordConfirmDialog>
</template>
