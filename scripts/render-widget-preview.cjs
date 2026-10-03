// Developer-only visual QA. npm install --prefix artifacts/widget-preview adaptivecards@3.0.5 playwright@1.56.1
// node scripts/render-widget-preview.cjs <cards-directory> <node_modules-directory> <output-directory>
const fs = require('node:fs');
const path = require('node:path');
const [cardsDirectory, modulesDirectory, outputDirectory] = process.argv.slice(2).map(p => path.resolve(p));
const { chromium } = require(path.join(modulesDirectory, 'playwright'));
(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 300, height: 480 }, deviceScaleFactor: 1 });
    await page.setContent('<html><body style="margin:0;font-family:Segoe UI;background:transparent;color:white"><div id="widget" style="background:#202020;border-radius:12px;overflow:hidden"><header style="padding:12px 16px;font-size:14px;font-weight:600">Ai UsageNest</header><main id="card"></main></div></body></html>');
    await page.addScriptTag({ path: path.join(modulesDirectory, 'adaptivecards', 'dist', 'adaptivecards.js') });
    const report = [];
    for (const size of ['small', 'medium', 'large']) {
      const card = JSON.parse(fs.readFileSync(path.join(cardsDirectory, `${size}.json`), 'utf8'));
      const result = await page.evaluate(card => {
        const host = document.getElementById('card'); host.replaceChildren();
        const adaptiveCard = new AdaptiveCards.AdaptiveCard();
        adaptiveCard.hostConfig = new AdaptiveCards.HostConfig({ fontFamily: 'Segoe UI', supportsInteractivity: true,
          spacing: { small: 4, default: 8, medium: 12, large: 16, extraLarge: 20, padding: 12 },
          fontSizes: { small: 12, default: 14, medium: 17, large: 21, extraLarge: 28 },
          containerStyles: { default: { backgroundColor: '#202020', foregroundColors: {
            default: { default: '#FFFFFF', subtle: '#CCCCCC' }, accent: { default: '#B4A8FF', subtle: '#B4A8FF' }
          } } }, actions: { maxActions: 2, spacing: 'small', buttonSpacing: 8, actionsOrientation: 'horizontal' }
        });
        adaptiveCard.parse(card); host.append(adaptiveCard.render());
        return { height: host.getBoundingClientRect().height, errors: adaptiveCard.validateProperties().validationEvents.map(e => e.message) };
      }, card);
      const height = Math.ceil(result.height + 43);
      await page.setViewportSize({ width: 300, height });
      await page.screenshot({ path: path.join(outputDirectory, `${size}.png`), omitBackground: true });
      report.push({ size, width: 300, height, errors: result.errors });
      if (size === 'medium') {
        if (height > 304) throw new Error('Medium widget does not fit the picker preview.');
        await page.evaluate(() => document.getElementById('widget').style.height = '304px');
        await page.setViewportSize({ width: 300, height: 304 });
        await page.screenshot({ path: path.join(outputDirectory, 'Overview.png'), omitBackground: true });
        await page.evaluate(() => document.getElementById('widget').style.height = 'auto');
      }
    }
    fs.writeFileSync(path.join(outputDirectory, 'render-report.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
