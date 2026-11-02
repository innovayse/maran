import { useApi } from "../useApi";
import type { Server, ServersApi } from "../../types/server";

/** The endpoint the servers the panel manages are read from. */
const SERVERS_PATH = "/api/v1/servers";

/**
 * Builds the servers API on top of the shared low-level client (issue #319).
 *
 * One read and nothing else: there is no switch call because the panel manages one server today,
 * and the fleet work (issue #180) decides what choosing a server means.
 * @returns The {@link ServersApi} bound to the panel's servers endpoint.
 */
export const useServersApi = (): ServersApi => {
  const api = useApi();

  /**
   * Lists the servers the panel manages.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The servers, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<Server[]> => {
    return api.get<Server[]>(SERVERS_PATH, signal);
  };

  return { list };
};
