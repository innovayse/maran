import { expect, test, type Page } from "@playwright/test";
import { stubSignedIn, stubbedAdministrator } from "../fixtures/stub-auth-routes";
import { stubEmptyModules } from "../fixtures/stub-modules-route";
import { stubHealthy } from "../fixtures/stub-health-route";
import { stubServers } from "../fixtures/stub-servers-route";
import type { Server } from "../../src/types/server";

/** The one server the panel manages today: the machine it runs on, named by its configured host. */
const LOCAL_SERVER: Server = { kindDisplayName: "This server (local)", hostName: "panel.example.com" };

/**
 * Signs a person in with the given role and answers the shell's own requests.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @param servers The servers the panel reports.
 * @returns A function reporting how many times the server list was requested.
 */
const stubPanel = async (page: Page, role: "admin" | "customer", servers: Server[]): Promise<() => number> => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await stubSignedIn(page, { ...stubbedAdministrator, role });
  await stubHealthy(page);
  await stubEmptyModules(page);
  return stubServers(page, servers);
};

/**
 * A second server, as fleet mode (issue #180) will report one. The panel cannot report it today; the
 * stub stands in so the display that fleet mode needs is exercised before the fleet exists.
 */
const SECOND_SERVER: Server = { kindDisplayName: "This server (local)", hostName: "node-2.example.com" };

test("with one server there is nothing to select, so no selector is drawn", async ({ page }) => {
  const requested = await stubPanel(page, "admin", [LOCAL_SERVER]);

  await page.goto("/");

  // The list WAS read (the count below), so the absence is the single-server rule and not a read
  // that never happened; the two-server case below proves the same banner can carry the selector.
  await expect(page.getByRole("banner")).toBeVisible();
  await expect.poll(requested).toBeGreaterThan(0);
  await expect(page.getByTestId("shell-server-selector")).toHaveCount(0);
});

test("with two servers an administrator sees the current one named in the app bar", async ({ page }) => {
  await stubPanel(page, "admin", [LOCAL_SERVER, SECOND_SERVER]);

  await page.goto("/");

  const selector = page.getByRole("banner").getByRole("group", { name: "Current server: panel.example.com" });
  await expect(selector).toBeVisible();
  await expect(selector).toContainText("panel.example.com");
});

test("a server with no configured host name shows the kind instead of a made-up name", async ({ page }) => {
  await stubPanel(page, "admin", [{ ...LOCAL_SERVER, hostName: null }, SECOND_SERVER]);

  await page.goto("/");

  await expect(
    page.getByRole("banner").getByRole("group", { name: "Current server: This server (local)" }),
  ).toBeVisible();
});

test("a customer is not shown the server and the list is never requested", async ({ page }) => {
  const requested = await stubPanel(page, "customer", [LOCAL_SERVER]);

  await page.goto("/");

  // The shell has drawn (the banner is there), so the absence below is a measurement and not a page
  // that had not finished: the administrator case above proves this same banner does carry it.
  await expect(page.getByRole("banner")).toBeVisible();
  await expect(page.getByTestId("shell-server-selector")).toHaveCount(0);
  expect(requested()).toBe(0);
});
