// Regenerates docs/dashboard.png and docs/install-prompt.png, the two images the Marketplace
// listing points at.
//
//   dotnet run --project tests/TestSite            # in one shell
//   node tests/TestSite/screenshots.mjs            # in another
//
// Both shots are of the shipped UI. Nothing here restyles the component or rewrites its copy; the
// only thing supplied is data, and the reason each one needs it is noted below.

import { chromium } from 'playwright';

const site = process.argv[2] ?? 'http://127.0.0.1:5199';
const demo = process.argv[3] ?? 'https://dev-playground.baryo.dev';

const browser = await chromium.launch();

// ---- dashboard --------------------------------------------------------------------------------
// The component reads its bearer token from window.Umbraco, which exists only inside the backoffice
// shell, so every call from the preview page is a 401. The three responses are supplied here rather
// than driving a real sign-in: the numbers below are what a seeded database produced, and the
// rendering, formatting and truncation are entirely the component's.
{
  const page = await browser.newPage({
    viewport: { width: 1200, height: 900 },
    deviceScaleFactor: 2,
  });

  const day = (n) => new Date(Date.UTC(2026, 8, n)).toISOString();
  const devices = [
    ['seed-8-ios', 'ios', 12], ['seed-7-android', 'android', 5], ['seed-6-ios', 'ios', 7],
    ['seed-5-macos', 'macos', 3], ['seed-4-windows', 'windows', 4], ['seed-3-android', 'android', 6],
    ['seed-2-ios', 'ios', 8], ['seed-1-android', 'android', 10],
  ].map(([deviceId, platform, launchCount]) => ({
    deviceId, platform, displayMode: 'standalone', installed: true,
    firstSeenAt: day(14), lastSeenAt: day(22), installedAt: day(14), launchCount,
  }));

  const api = '**/umbraco/management/api/v1/baryodev/pwa';
  await page.route(`${api}/summary`, (route) => route.fulfill({
    json: {
      installed: 8,
      totalDevices: 31,
      activeLast30Days: 8,
      byPlatform: { android: 3, ios: 3, windows: 1, macos: 1 },
    },
  }));
  await page.route(`${api}/readiness`, (route) =>
    route.fulfill({ json: { installable: true, checks: [] } }));
  await page.route(`${api}/installs*`, (route) => route.fulfill({ json: devices }));

  await page.goto(`${site}/dashboard-preview.html`, { waitUntil: 'networkidle' });
  await page.locator('baryodev-pwa-dashboard tbody tr').first().waitFor({ timeout: 15000 });
  await page.waitForTimeout(1200);
  await page.locator('.shell').screenshot({ path: 'docs/dashboard.png' });
  console.log('docs/dashboard.png');
  await page.close();
}

// ---- install prompt ---------------------------------------------------------------------------
// The live demo, which is where the listing sends people. beforeinstallprompt does not fire in
// headless Chromium, so the event is dispatched the way a qualifying browser would; the banner it
// builds is the shipped script's, unmodified.
//
// The viewport is the shot rather than the full page. The banner is position:fixed at bottom:12px,
// so a full-page capture strands it in the middle of the document over the copy, which is not
// where anybody sees it.
{
  const page = await browser.newPage({
    viewport: { width: 1180, height: 1000 },
    deviceScaleFactor: 2,
    userAgent:
      'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) '
      + 'Chrome/140.0.0.0 Mobile Safari/537.36',
  });

  await page.goto(demo, { waitUntil: 'networkidle' });
  await page.evaluate(() => {
    const event = new Event('beforeinstallprompt');
    event.prompt = () => Promise.resolve();
    event.userChoice = Promise.resolve({ outcome: 'accepted' });
    window.dispatchEvent(event);
  });

  await page.locator('#bd-pwa-install').waitFor({ timeout: 5000 });
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'docs/install-prompt.png' });
  console.log('docs/install-prompt.png');
  await page.close();
}

await browser.close();
