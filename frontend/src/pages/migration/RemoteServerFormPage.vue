<script setup lang="ts">
/**
 * The remote server form, for adding a server and for changing one (the route carries `id` only in
 * the second case). `UiForm` renders `<form novalidate>`, so the browser validates nothing and
 * every message the person reads comes either from this page or from the server (rules/vue.md).
 *
 * The client-side rules are advice that saves a round trip, and they err permissive: the panel
 * validates every field again and its already-localized refusal is shown as it was sent.
 *
 * **Adding does work.** The panel generates the key and reads the server's host keys before it
 * answers, which takes seconds, so the submit control says so while it waits. A server whose host
 * keys cannot be read is not stored, and the refusal says why.
 *
 * **Changing the target forgets the host key.** The form says so in a hint, because the
 * administrator must confirm the key again; the connection is not tested here.
 * Renders a `<section>`, not a `<main>`.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFieldHint from '../../components/ui/UiFieldHint.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import { useRemoteServersStore } from '../../stores/remoteServers'
import { isHostname, isIpv4, isIpv6 } from '../../utils/dnsAddress'
import { flashNoticeState } from '../../utils/flashNotice'
import { isUsableCidr } from '../../utils/isUsableCidr'
import type { LoadingState } from '../../types/loading'
import type { RemoteServerRequest } from '../../types/migrationSource'

/** Props accepted by this page, bound from the route. */
const props = withDefaults(
  defineProps<{
    /** The server being changed, from `/migration/remote-servers/:id/edit`; absent when adding. */
    id?: string
  }>(),
  { id: undefined },
)

/** The login grammar the remote side accepts: lowercase letters, digits, `-` and `_`, not starting with a digit. */
const LOGIN_PATTERN = /^[a-z_][a-z0-9_-]{0,31}$/

/** The SSH port a new server starts with. */
const DEFAULT_PORT = '22'

const { t } = useI18n()
const router = useRouter()
const store = useRemoteServersStore()

/** The name as typed. */
const name: Ref<string> = ref('')

/** The host name or address as typed. */
const host: Ref<string> = ref('')

/** The SSH port as typed. */
const port: Ref<string> = ref(DEFAULT_PORT)

/** The remote login as typed. */
const sshUser: Ref<string> = ref('')

/** The address limit as typed; empty for none. */
const allowedFrom: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Whether this page changes an existing server. */
const isEdit: ComputedRef<boolean> = computed(() => {
  return props.id !== undefined
})

/** Where the read of the server being changed stands; an add has nothing to read. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  return isEdit.value ? store.currentState : 'loaded'
})

/** Whether the server being changed does not exist: the read finished and found nothing. */
const missing: ComputedRef<boolean> = computed(() => {
  return isEdit.value && store.currentState === 'loaded' && store.current === null
})

/** The addresses and ranges typed in the limit field, split on commas and spaces. */
const allowedItems: ComputedRef<string[]> = computed(() => {
  return allowedFrom.value.split(/[\s,]+/).filter((item) => {
    return item !== ''
  })
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  return submitted.value && name.value.trim() === '' ? t('migration.form.errors.nameRequired') : null
})

/** Validation message for the host, or `null`. */
const hostError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = host.value.trim()
  if (value === '') {
    return t('migration.form.errors.hostRequired')
  }
  return isHostname(value) || isIpv4(value) || isIpv6(value) ? null : t('migration.form.errors.hostInvalid')
})

/** Validation message for the port, or `null`. */
const portError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = Number(port.value.trim())
  return /^\d+$/.test(port.value.trim()) && value >= 1 && value <= 65535 ? null : t('migration.form.errors.portInvalid')
})

/** Validation message for the login, or `null`. */
const sshUserError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = sshUser.value.trim()
  if (value === '') {
    return t('migration.form.errors.sshUserRequired')
  }
  return LOGIN_PATTERN.test(value) ? null : t('migration.form.errors.sshUserInvalid')
})

/** Validation message for the address limit, or `null`. A single address is as good as a range. */
const allowedFromError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const valid = allowedItems.value.every((item) => {
    return isIpv4(item) || isIpv6(item) || isUsableCidr(item)
  })
  return valid ? null : t('migration.form.errors.allowedFromInvalid')
})

/** Where Cancel leads: the server being changed, or the list when adding. */
const cancelTarget: ComputedRef<RouteLocationRaw> = computed(() => {
  return props.id === undefined ? { name: 'remote-servers' } : { name: 'remote-server-detail', params: { id: props.id } }
})

/** The refusal of the last add or edit, shown verbatim above the form. */
const refusal: ComputedRef<string | null> = computed(() => {
  return store.saveErrorMessage
})

/**
 * Copies the server being changed into the fields.
 * @returns Nothing.
 */
const fillFromServer = (): void => {
  const server = store.current
  if (server === null) {
    return
  }
  name.value = server.name
  host.value = server.host
  port.value = String(server.port)
  sshUser.value = server.sshUser
  allowedFrom.value = server.allowedFrom ?? ''
}

/**
 * Validates, then submits. On success the server's page opens with a notice, where the next step
 * (put the key on the server, confirm its host key) waits.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (
    nameError.value !== null ||
    hostError.value !== null ||
    portError.value !== null ||
    sshUserError.value !== null ||
    allowedFromError.value !== null
  ) {
    return
  }

  const request: RemoteServerRequest = {
    name: name.value.trim(),
    host: host.value.trim(),
    port: Number(port.value.trim()),
    sshUser: sshUser.value.trim(),
    allowedFrom: allowedItems.value.length === 0 ? null : allowedItems.value.join(', '),
  }
  const saved = props.id === undefined ? await store.add(request) : await store.edit(props.id, request)
  if (saved !== null) {
    const notice = isEdit.value
      ? t('migration.form.saved', { name: saved.name })
      : t('migration.form.created', { name: saved.name })
    await router.push({ name: 'remote-server-detail', params: { id: saved.id }, state: flashNoticeState(notice) })
  }
}

onMounted(async () => {
  store.clearErrors()
  if (props.id !== undefined) {
    await store.loadOne(props.id)
    fillFromServer()
  }
})
</script>

<template>
  <section class="flex w-full max-w-3xl min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="regionState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="missing"
        :title="t('migration.form.notFoundTitle')"
        :description="t('migration.form.notFoundDescription')"
      />

      <div v-else class="flex min-w-0 flex-col gap-4">
        <UiPageHeading
          :title="isEdit ? t('migration.form.headingEdit') : t('migration.form.headingNew')"
          :subtitle="isEdit ? t('migration.form.subtitleEdit') : t('migration.form.subtitleNew')"
        />

        <!-- The one thing that needs attention goes first: the server's own refusal, verbatim. -->
        <UiAlert v-if="refusal !== null" variant="error">{{ refusal }}</UiAlert>

        <UiCard>
          <UiForm @submit="submit">
            <div class="flex flex-col gap-5">
              <div>
                <UiInput
                  v-model="name"
                  :label="t('migration.form.fields.name')"
                  :placeholder="t('migration.form.placeholders.name')"
                  :error="nameError"
                  required
                />
                <UiFieldHint class="mt-1.5">{{ t('migration.form.hints.name') }}</UiFieldHint>
              </div>
              <div>
                <UiInput
                  v-model="host"
                  :label="t('migration.form.fields.host')"
                  :placeholder="t('migration.form.placeholders.host')"
                  :error="hostError"
                  mono
                  required
                />
                <UiFieldHint class="mt-1.5">{{ t('migration.form.hints.host') }}</UiFieldHint>
              </div>
              <div class="grid gap-5 sm:grid-cols-2">
                <UiInput
                  v-model="port"
                  :label="t('migration.form.fields.port')"
                  :placeholder="t('migration.form.placeholders.port')"
                  :error="portError"
                  inputmode="numeric"
                  mono
                  required
                />
                <UiInput
                  v-model="sshUser"
                  :label="t('migration.form.fields.sshUser')"
                  :placeholder="t('migration.form.placeholders.sshUser')"
                  :error="sshUserError"
                  autocomplete="off"
                  mono
                  required
                />
              </div>
              <UiFieldHint>{{ t('migration.form.hints.sshUser') }}</UiFieldHint>
              <div>
                <UiInput
                  v-model="allowedFrom"
                  :label="t('migration.form.fields.allowedFrom')"
                  :placeholder="t('migration.form.placeholders.allowedFrom')"
                  :error="allowedFromError"
                  mono
                />
                <UiFieldHint class="mt-1.5">{{ t('migration.form.hints.allowedFrom') }}</UiFieldHint>
              </div>
              <UiAlert v-if="isEdit" variant="info">{{ t('migration.form.hints.retarget') }}</UiAlert>
              <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
                <UiButton variant="secondary" :to="cancelTarget">{{ t('common.cancel') }}</UiButton>
                <UiButton type="submit" :disabled="store.saving">
                  {{
                    store.saving
                      ? isEdit
                        ? t('migration.form.workingEdit')
                        : t('migration.form.workingNew')
                      : isEdit
                        ? t('migration.form.submitEdit')
                        : t('migration.form.submitNew')
                  }}
                </UiButton>
              </div>
            </div>
          </UiForm>
        </UiCard>
      </div>
    </UiLoadingRegion>
  </section>
</template>
