<script setup lang="ts">
/**
 * The one place a module operation is confirmed and started. A screen that offers an install, an
 * update, a removal or a rollback sets `request` and mounts this component; it shows the right
 * dialog ({@link InstallModuleDialog}, {@link RemoveModuleDialog} or a rollback password dialog),
 * reads the module's manifest summary for an install or update, sends the operation through the
 * marketplace store and closes itself once the panel accepted it. Progress is then the store's
 * (`ModuleOperationProgress`); a refusal stays on screen inside the dialog in the panel's words.
 *
 * Keeping this in one component means the four operations are confirmed the same way from every
 * screen (the cards, the Installed rows, the Updates rows, the detail page, a failed operation's
 * retry), and the password handling is not repeated.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMarketplaceStore } from '../../stores/marketplace'
import type {
  InstallModuleRequest,
  MarketplaceModuleDetail,
  ModuleManifestSummary,
  ModuleOperationRequest,
} from '../../types/marketplace'
import InstallModuleDialog from './InstallModuleDialog.vue'
import PasswordConfirmDialog from './PasswordConfirmDialog.vue'
import RemoveModuleDialog from './RemoveModuleDialog.vue'

/** Props accepted by {@link ModuleOperationDialog}. */
const props = defineProps<{
  /** The operation to confirm, or `null` while no dialog is wanted. */
  request: ModuleOperationRequest | null
}>()

/** Events emitted by {@link ModuleOperationDialog}. */
const emit = defineEmits<{
  /** The dialog should go away: declined, or the panel accepted the operation. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const store = useMarketplaceStore()

/** The last non-null request, kept so the dialog's text does not vanish while it fades out. */
const shown: Ref<ModuleOperationRequest | null> = ref(props.request)

/** The module's detail as the store holds it, or `undefined` while it has not been read. */
const detail: ComputedRef<MarketplaceModuleDetail | undefined> = computed(() => {
  return shown.value === null ? undefined : store.details[shown.value.id]
})

/** The version the operation is for: the one asked for, else the newest the catalogue offers. */
const version: ComputedRef<string> = computed(() => {
  return shown.value?.version ?? detail.value?.module.latestVersion ?? ''
})

/** The manifest summary to show, or `null` when the panel holds none or no version is known. */
const manifest: ComputedRef<ModuleManifestSummary | null> = computed(() => {
  return version.value === '' ? null : (detail.value?.manifest ?? null)
})

/** Whether a request for this module is in flight. */
const acting: ComputedRef<boolean> = computed(() => {
  return shown.value !== null && store.busy[shown.value.id] === true
})

/** The panel's refusal as the dialog prints it; a missing answer uses the panel-owned "unreachable" sentence. */
const errorMessage: ComputedRef<string | null> = computed(() => {
  if (store.errorMessage !== null) {
    return store.errorMessage
  }
  return store.unreachable ? t('marketplace.unreachable.description') : null
})

/** Whether the dialog is open. */
const open: ComputedRef<boolean> = computed(() => {
  return props.request !== null
})

/**
 * Starts the operation and closes the dialog when the panel accepted it.
 * @param start Sends the operation through the store.
 * @returns Resolves once the request settled.
 */
const run = async (start: () => Promise<boolean>): Promise<void> => {
  if (await start()) {
    emit('close')
  }
}

/**
 * Starts an install or an update with the typed acknowledgement and password.
 * @param body The request body the install dialog built.
 * @returns Resolves once the request settled.
 */
const submitInstall = (body: InstallModuleRequest): Promise<void> => {
  const current = shown.value
  if (current === null) {
    return Promise.resolve()
  }
  return run(() => {
    return current.kind === 'update' ? store.update(current.id, body) : store.install(current.id, body)
  })
}

/**
 * Starts a removal or a rollback with the typed password.
 * @param password The administrator's current password.
 * @returns Resolves once the request settled.
 */
const submitPassword = (password: string): Promise<void> => {
  const current = shown.value
  if (current === null) {
    return Promise.resolve()
  }
  return run(() => {
    return current.kind === 'rollback' ? store.rollback(current.id, password) : store.remove(current.id, password)
  })
}

/**
 * Closes the dialog and forgets the refusal it showed.
 * @returns Nothing.
 */
const dismiss = (): void => {
  store.clearError()
  emit('close')
}

watch(
  (): ModuleOperationRequest | null => {
    return props.request
  },
  (next: ModuleOperationRequest | null): void => {
    if (next === null) {
      return
    }
    shown.value = next
    store.clearError()
    if ((next.kind === 'install' || next.kind === 'update') && store.details[next.id] === undefined) {
      void store.loadDetails(next.id)
    }
  },
  { immediate: true },
)
</script>

<template>
  <InstallModuleDialog
    v-if="shown !== null && (shown.kind === 'install' || shown.kind === 'update')"
    :open="open"
    :mode="shown.kind"
    :module-name="shown.name"
    :version="version"
    :manifest="manifest"
    :manifest-loading="store.detailsLoading && detail === undefined"
    :acting="acting"
    :error-message="errorMessage"
    @close="dismiss"
    @submit="submitInstall"
  />
  <RemoveModuleDialog
    v-else-if="shown !== null && shown.kind === 'remove'"
    :open="open"
    :module-name="shown.name"
    :acting="acting"
    :error-message="errorMessage"
    @close="dismiss"
    @submit="submitPassword"
  />
  <PasswordConfirmDialog
    v-else-if="shown !== null"
    :open="open"
    :title="t('marketplace.rollback.title', { name: shown.name })"
    :question="t('marketplace.rollback.question')"
    :submit-label="t('marketplace.rollback.submit')"
    :acting="acting"
    :acting-label="t('marketplace.switch.working')"
    :error-message="errorMessage"
    @close="dismiss"
    @submit="submitPassword"
  />
</template>
