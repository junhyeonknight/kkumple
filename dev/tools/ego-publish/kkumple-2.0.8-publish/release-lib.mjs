// 릴리스 초안을 만들고 파일을 올린 뒤 게시하고, 공개 파일 해시를 확인한다.
export async function publishRelease(page, { tag, title, body, files, label, renames = {} }) {
  const crypto = await import("node:crypto");
  const fs = await import("node:fs/promises");
  await page.goto("https://github.com/junhyeonknight/kkumple/releases/new", { timeout: 60000 });
  await page.waitForSelector('button[aria-label="Tag: Select tag"]', { timeout: 30000 });
  const user = await page.evaluate(() => document.querySelector('meta[name="user-login"]')?.content || null);
  if (user !== "junhyeonknight") throw new Error("not signed in: " + user);
  await page.click('button[aria-label="Tag: Select tag"]', { label: "open tag picker" });
  await page.fill('input[placeholder="Search or create a new tag"]', tag);
  await page.waitForTimeout(600);
  await page.click('text="Create new tag"', { label: "create new tag" });
  await page.waitForSelector('input[aria-label="Tag name"]', { timeout: 10000 });
  let pos = await page.evaluate(() => {
    const b = [...document.querySelectorAll('[role=dialog] button, dialog button')].find((x) => x.innerText.trim() === "Create" && x.getClientRects().length);
    const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
  });
  await page.mouse.click(pos.x, pos.y, { label: "confirm new tag" });
  await page.waitForTimeout(800);
  await page.fill('input[name="release[name]"]', title);
  await page.fill('textarea[name="release[body]"]', body);
  await page.setInputFiles("#releases-upload", files.map((f) => f.path));
  const want = files.map((f) => f.asset);
  let s;
  for (let i = 0; i < 120; i++) {
    await page.waitForTimeout(5000);
    s = await page.evaluate(() => ({
      assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((x) => x.value).filter(Boolean),
      busy: /Uploading/i.test(document.body.innerText) || !!document.querySelector('.is-uploading'),
    }));
    if (s.assets.length >= files.length && !s.busy) break;
  }
  // GitHub가 바꾼 이름(한글 등)을 원하는 이름으로 고친다.
  for (const [from, to] of Object.entries(renames)) {
    const snap = (await page.snapshot({ scope: "full_page" })).split("\n");
    const i = snap.findIndex((l) => l.includes(`"${from}"`));
    const m = i > 0 && snap[i - 1].match(/textbox "Change this file’s name" \[ref=(\d+)/);
    if (!m) throw new Error("rename target not found: " + from);
    await page.fill("@" + m[1], to);
  }
  await page.evaluate((v) => document.querySelector(`input[name="release[release_label_option]"][value="${v}"]`).click(), label);
  await page.waitForTimeout(500);
  s = await page.evaluate(() => ({
    assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((x) => x.value).filter(Boolean),
    label: document.querySelector('input[name="release[release_label_option]"]:checked')?.value,
    tag: document.querySelector('input[name="release[tag_name]"]')?.value,
  }));
  if (s.tag !== tag || s.label !== label || want.some((a) => !s.assets.includes(a)) || s.assets.length !== want.length)
    throw new Error("draft check failed: " + JSON.stringify(s));
  pos = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find((x) => x.innerText.trim() === "Publish release" && x.getClientRects().length);
    b.scrollIntoView({ block: "center" }); const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
  });
  await page.mouse.click(pos.x, pos.y, { label: "publish release " + tag });
  await page.waitForURL("**/releases/tag/" + tag, { timeout: 60000 });
  const results = [];
  for (const f of files) {
    const local = await fs.readFile(f.path);
    const r = await fetch(`https://github.com/junhyeonknight/kkumple/releases/download/${tag}/${f.asset}`, { redirect: "follow" });
    const got = Buffer.from(await r.arrayBuffer());
    const ok = r.status === 200 && got.length === local.length &&
      crypto.createHash("sha256").update(got).digest("hex") === crypto.createHash("sha256").update(local).digest("hex");
    results.push({ asset: f.asset, ok });
  }
  return { url: await page.url(), results };
}
