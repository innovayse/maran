import { expect, test, type Page } from "@playwright/test";
import { stubSignedIn, stubbedAdministrator } from "../fixtures/stub-auth-routes";
import { stubEmptyModules } from "../fixtures/stub-modules-route";
import { stubHealthy } from "../fixtures/stub-health-route";
import { stubServers } from "../fixtures/stub-servers-route";
import type { Server } from "../../src/types/server";

/** The one server the panel manages today: the machine it runs on, named by its configured host. */
const LOCAL_SERVER: Server = { kind: "local", kindDisplayName: "This server (local)", hostName: "panel.example.com" };

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

test("an administrator sees the local server named in the app bar", async ({ page }) => {
  await stubPanel(page, "admin", [LOCAL_SERVER]);

  await page.goto("/");

  const selector = page.getByRole("banner").getByRole("group", { name: "Current server: panel.example.com" });
  await expect(selector).toBeVisible();
  await expect(selector).toContainText("panel.example.com");
});

test("with one server the selector is a display, not a menu", async ({ page }) => {
  await stubPanel(page, "admin", [LOCAL_SERVER]);

  await page.goto("/");

  const selector = page.getByTestId("shell-server-selector");
  await expect(selector).toBeVisible();
  // One server leaves nothing to choose, so nothing in it is a control.
  await expect(selector.getByRole("button")).toHaveCount(0);
  await expect(selector.getByRole("menu")).toHaveCount(0);
});

test("a panel with no configured host name shows the kind instead of a made-up name", async ({ page }) => {
  await stubPanel(page, "admin", [{ ...LOCAL_SERVER, hostName: null }]);

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
