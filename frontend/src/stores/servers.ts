import { defineStore } from "pinia";
import { computed, ref, type ComputedRef, type Ref } from "vue";
import { useServersApi } from "../composables/apis/useServersApi";
import { ApiError } from "../composables/useApi";
import type { Read } from "../types/loading";
import type { Server } from "../types/server";

/**
 * Owns the servers the panel manages, for the shell's server selector (issue #319). The selector
 * reads state from here and calls its actions; it never touches the API composable (rules/vue.md).
 *
 * No server is invented here: until the panel has answered, nothing is current and the selector
 * draws nothing rather than a plausible-looking name.
 */
export const useServersStore = defineStore("servers", () => {
  const api = useServersApi();

  /**
   * The servers the panel reported, in its order. A read nobody has asked for is `idle`; a refused
   * or unreachable one is `failed` unless a list was already held, which stays (see {@link load}).
   */
  const read: Ref<Read<Server[]>> = ref({ state: 'idle' });

  /** The servers held; empty before the first successful read. */
  const servers: ComputedRef<Server[]> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : [];
  });

  /**
   * The server every screen addresses. With one managed server that is simply the first entry:
   * there is nothing to choose between, so this is a fact about the data and not a stored choice.
   * Fleet mode (issue #180) replaces it with the server the operator selected, which is why it is
   * a getter here and no screen derives it for itself.
   */
  const current: ComputedRef<Server | null> = computed(() => {
    return servers.value[0] ?? null;
  });

  /**
   * Reads the servers the panel manages, replacing what is held. A failure keeps the previous list
   * so a transient error does not blank the app bar; with no list held yet it is a `failed` read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      read.value = { state: 'loaded', value: await api.list() };
    } catch (error) {
      // The selector is orientation, not a gate: a failed read is a display state, never a throw.
      if (read.value.state !== 'loaded') {
        read.value = { state: 'failed', message: error instanceof ApiError ? error.message : null };
      }
    }
  };

  return { servers, current, load };
});
