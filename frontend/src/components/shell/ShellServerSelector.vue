<script setup lang="ts">
/**
 * The app bar's server selector: which server every screen is addressing (issue #319).
 *
 * **Today it is a labelled display, not a menu.** The panel manages one server, the machine it runs
 * on, so there is nothing to choose between and a dropdown with one entry would be a control that
 * cannot do anything. It shows the current server with an accessible name that says what it is
 * ("Current server: panel.example.com"). When fleet mode (issue #180) gives the panel a second
 * server, this is the component that grows the menu; the data already arrives as a list.
 *
 * **Administrators only.** The list is operator knowledge and the endpoint refuses a customer, so
 * a customer's shell neither draws this nor asks for it. That check is presentation: the backend
 * is the boundary (rules/security.md), and the role flag errs permissive.
 *
 * **The name is the server's host name, as the panel reports it**; when the panel has no name
 * configured it reports none, and the kind ("This server (local)") stands in. Nothing here makes a
 * name up. The kind is localized by the backend, and the list is read again when the reader
 * switches language so it follows.
 *
 * A failed read draws nothing: this is orientation in the bar and not an error to shout on every
 * screen. Below the bar's narrow step the text gives way to the icon alone, whose group keeps the
 * full accessible name.
 */
import { computed, onMounted, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiIcon from '../ui/UiIcon.vue'
import { useCallerRole } from '../../composables/useCallerRole'
import { useLocaleStore } from '../../stores/locale'
import { useServersStore } from '../../stores/servers'

const { t } = useI18n()
const store = useServersStore()
const locale = useLocaleStore()
const { isAdmin } = useCallerRole()

/** What the current server is called: its host name, or the kind when the panel has no name for it. */
const name: ComputedRef<string | null> = computed(() => {
  const server = store.current
  return server === null ? null : (server.hostName ?? server.kindDisplayName)
})

/**
 * Reads the servers when the caller is an administrator.
 * @returns Resolves once the request has settled, or at once for a customer.
 */
const loadIfAdmin = async (): Promise<void> => {
  if (isAdmin.value) {
    await store.load()
  }
}

onMounted(loadIfAdmin)

// The kind's name is resolved by the backend in the language the request carried.
watch(
  () => {
    return locale.current
  },
  loadIfAdmin,
)
</script>

<template>
  <div
    v-if="isAdmin && name !== null"
    role="group"
    data-testid="shell-server-selector"
    class="flex h-control min-w-0 shrink-0 items-center gap-2 rounded-control px-2 text-text-secondary"
    :aria-label="t('servers.selector.current', { name })"
  >
    <UiIcon name="server" size="lg" />
    <span class="max-w-48 truncate text-sm @max-[30rem]:hidden" :title="name">{{ name }}</span>
  </div>
</template>
