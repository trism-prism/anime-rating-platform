// Regression for the prefill race: a quick rate made before GET /me/ratings answers must keep the existing
// review text, spoiler flag and audience (Friends) when saved. Also the slider keyboard fix and aria bits.
// Run: start the API and `npm run dev -- --port 5199`, seed a title with id 2, register tester1@localhost.test (verified),
// then `node scripts/rate-prefill-race.mjs` with Playwright resolvable (playwright is not a dependency of this repo).
import { chromium } from 'playwright'
const BASE = 'http://localhost:5199'
const launch = () => chromium.launch({ channel: 'chrome', headless: true })
const settle = (page) => page.waitForLoadState('networkidle').catch(() => {})
const API = 'http://localhost:5276'
const login = await fetch(API + '/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: 'tester1@localhost.test', password: 'Testpass!2345' }) }).then((r) => r.json())
const put = (body) => fetch(API + '/titles/2/rating', { method: 'PUT', headers: { 'content-type': 'application/json', authorization: 'Bearer ' + login.accessToken }, body: JSON.stringify(body) })
console.log('seed existing rating:', (await put({ story: 6, art: 7, characters: 8, enjoyment: 9, reviewText: 'Private thoughts for friends.', audience: 'Friends', spoiler: true })).status)
const mine = async () => (await fetch(API + '/me/ratings', { headers: { authorization: 'Bearer ' + login.accessToken } }).then((r) => r.json())).find((r) => r.titleId === 2)
let fail = 0
const check = (name, ok) => { console.log(ok ? 'PASS' : 'FAIL', name); if (!ok) fail++ }

const browser = await launch()
const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage()
await page.goto(BASE + '/login')
await page.getByLabel(/email/i).fill('tester1@localhost.test')
await page.locator('input[type=password]').first().fill('Testpass!2345')
await page.locator('form button[type=submit]').click()
await page.waitForURL((u) => u.pathname === '/'); await settle(page)

// hold the prefill answer back for 2.5 s
await page.route('**/api/me/ratings', async (route) => { await new Promise((r) => setTimeout(r, 2500)); route.continue() })
await page.evaluate(() => { history.pushState({}, '', '/titles/2'); dispatchEvent(new PopStateEvent('popstate')) })
await page.waitForSelector('.quick-opt')
await page.locator('.quick-opt', { hasText: /^9$/ }).click() // quick rate before the prefill returns
check('Save disabled while the existing rating is still loading', await page.getByRole('button', { name: /^(Save|Update)$/ }).isDisabled())
await page.waitForFunction(() => !document.querySelector('.rate-actions .btn-primary')?.disabled, null, { timeout: 8000 })
check('review text restored after the prefill', (await page.locator('textarea[aria-label=Review]').inputValue()) === 'Private thoughts for friends.')
check('quick score kept (9)', (await page.locator('.quick-opt input:checked').inputValue()) === '9')
await page.getByRole('button', { name: /^(Save|Update)$/ }).click(); await page.waitForTimeout(800)
const after = await mine()
check('server kept the review text', after.reviewText === 'Private thoughts for friends.')
check('server kept audience Friends', after.audience === 'Friends')
check('server kept spoiler', after.spoiler === true)
check('server has the new scores (9)', after.story === 9 && after.enjoyment === 9)

// sliders: unset slider, first arrow press sets 5, second moves
await page.unroute('**/api/me/ratings')
await page.getByRole('button', { name: 'Remove', exact: true }).click(); await page.locator('dialog .btn-danger').click(); await page.waitForTimeout(800)
await page.getByRole('button', { name: /Rate in detail/ }).click()
const art = page.locator('.rate-detail input[type=range]').nth(1)
await art.focus()
check('unset valuetext', (await art.getAttribute('aria-valuetext')) === 'Not set')
await page.keyboard.press('ArrowRight')
check('first arrow sets 5', (await art.inputValue()) === '5' && (await art.getAttribute('aria-valuetext')) === '5')
await page.keyboard.press('ArrowRight')
check('second arrow moves to 6', (await art.inputValue()) === '6')
await page.getByRole('button', { name: 'Rate in detail', exact: true }).click()
check('aria-controls resolves while closed', await page.evaluate(() => { const b = [...document.querySelectorAll('button')].find((x) => x.textContent.startsWith('Rate in detail')); return !!document.getElementById(b.getAttribute('aria-controls')) }))
check('toggle name has no Show/Hide', (await page.getByRole('button', { name: 'Rate in detail', exact: true }).count()) === 1)
check('quick scale described', !!(await page.locator('fieldset.quick').getAttribute('aria-describedby')))
await browser.close()
console.log(fail ? `${fail} FAILED` : 'all passed')
process.exit(fail ? 1 : 0)
