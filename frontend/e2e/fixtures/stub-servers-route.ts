import type { Page } from "@playwright/test";
import type { Server } from "../../src/types/server";

/** The servers the panel manages. */
const SERVERS_PATTERN = /\/api\/v1\/servers$/;

/**
 * Fulfils `GET /api/v1/servers` with the given servers.
 * @param page The Playwright page whose network the route is installed on.
 * @param servers The servers the panel reports.
 * @returns A function reporting how many times the list has been requested so far.
 */
export const stubServers = async (page: Page, servers: Server[]): Promise<() => number> => {
  let requests = 0;

  await page.route(SERVERS_PATTERN, async (route) => {
    requests += 1;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(servers),
    });
  });

  return (): number => {
    return requests;
  };
};
