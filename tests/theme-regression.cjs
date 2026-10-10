const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../src/scada_demo_test.Web/wwwroot/js/theme.js'), 'utf8');
for (const saved of [null, 'light', 'dark']) {
  for (const systemDark of [false, true]) {
    let preference = saved;
    let listener;
    const attributes = {};
    const context = {
      localStorage: { getItem: () => preference, setItem: (_, value) => { preference = value; } },
      document: { documentElement: { setAttribute: (key, value) => { attributes[key] = value; } }, body: { classList: { toggle() {} } } },
      window: { matchMedia: () => ({ matches: systemDark, addEventListener: (_, callback) => { listener = callback; } }) }
    };
    vm.runInNewContext(source, context);
    const expected = saved ?? (systemDark ? 'dark' : 'light');
    assert.equal(attributes['data-theme'], expected, 'pre-paint theme');
    assert.equal(context.window.theme.initTheme(), expected === 'dark');
    listener({ matches: !systemDark });
    assert.equal(attributes['data-theme'], saved ?? (!systemDark ? 'dark' : 'light'));
    context.window.theme.setTheme(true);
    assert.equal(preference, 'dark');
    assert.equal(attributes['data-theme'], 'dark');
    listener({ matches: false });
    assert.equal(attributes['data-theme'], 'dark', 'explicit preference must win');
  }
}
console.log('Theme regression: six preference scenarios passed.');
const root = path.join(__dirname, '../src/scada_demo_test.Web');
const host = fs.readFileSync(path.join(root, 'Pages/_Host.cshtml'), 'utf8');
assert.match(host, /href="scada_demo_test\.Web\.styles\.css"/, 'isolated content CSS must load');
assert.match(host, /href="css\/site\.css" asp-append-version="true"/, 'theme assets must invalidate browser caches');
const navigation = fs.readFileSync(path.join(root, 'Shared/NavMenu.razor'), 'utf8');
assert.match(navigation, /new NavItem\("\/infrastructure", "View Gateway and Sensors"/, 'new page must be a standalone navigation item');
assert.equal((navigation.match(/"\/infrastructure"/g) || []).length, 1, 'do not duplicate the tab in a collapsed group');
const page = fs.readFileSync(path.join(root, 'Pages/Infrastructure.razor'), 'utf8');
assert.match(page, /@page "\/infrastructure"/);
assert.match(page, /GetGatewayDevicesAsync/);
assert.match(page, /GetSensorsAsync/);
assert.match(page, /GetMeterSnapshot\(s\.UniqueSensorId\)/);
assert.doesNotMatch(page, /Math\.random|new Random\(|Simulated data/);
console.log('Navigation, CSS loading and real-data source contracts passed.');
