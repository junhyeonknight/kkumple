const task = await taskSpace(1);
const page = task.page("p1");
const snap = await page.snapshot({ scope: "full_page" });
const lines = snap.split("\n");
const i = lines.findIndex((l) => /binar/i.test(l));
console.log(lines.slice(Math.max(0, i - 5), i + 25).join("\n"));
console.log(await page.evaluate(() => [...document.querySelectorAll('label[for="releases-upload"], [aria-label*="binaries" i]')].map((e) => ({ tag: e.tagName, text: e.innerText.trim().slice(0, 80), vis: !!e.offsetParent, rect: JSON.stringify(e.getBoundingClientRect()) }))));
