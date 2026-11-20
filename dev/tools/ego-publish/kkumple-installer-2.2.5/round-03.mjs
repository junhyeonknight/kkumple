const task = await taskSpace(2);
const page = task.page("p1");
const snap = await page.snapshot({ scope: "full_page" });
const lines = snap.split("\n");
const idx = lines.findIndex((l) => l.includes('"-.-2.2.5.zip"') || l.includes("-.-2.2.5.zip"));
let ref = null;
for (let i = idx; i >= Math.max(0, idx - 3); i--) { const m = lines[i].match(/textbox "Change this file’s name" \[ref=(\d+)/); if (m) { ref = m[1]; break; } }
console.log({ idx, line: lines[idx], prev: lines[idx - 1], ref });
if (!ref) throw new Error("rename box not found");
await page.fill("@" + ref, "kkumple-install-2.2.5.zip");
const body = await page.evaluate(() => document.querySelector('textarea[name="release[body]"]').value);
await page.fill('textarea[name="release[body]"]', body.replace("꿈플-배포용-2.2.5.zip을", "kkumple-install-2.2.5.zip을"));
await page.evaluate(() => document.querySelector('input[name="release[release_label_option]"][value="none"]').click());
await page.waitForTimeout(500);
const s = await page.evaluate(() => ({
  assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((x) => x.value + " " + (x.parentElement?.innerText.trim() || "")).filter((x) => x.trim()),
  label: document.querySelector('input[name="release[release_label_option]"]:checked')?.value,
  tag: document.querySelector('input[name="release[tag_name]"]')?.value,
  title: document.querySelector('input[name="release[name]"]')?.value,
  body: document.querySelector('textarea[name="release[body]"]')?.value,
}));
console.log(s);
