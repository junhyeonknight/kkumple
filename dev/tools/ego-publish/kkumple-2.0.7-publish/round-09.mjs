const task = await taskSpace(1);
const page = task.page("p1");
const s = await page.evaluate(() => ({
  assets: [...document.querySelectorAll('input[aria-label="Change this file’s name"]')].map((i) => i.value + " " + (i.parentElement?.innerText.trim() || "")),
  failed: [...document.querySelectorAll('.is-failed, .is-bad-file, .is-too-big, .is-empty')].filter((e) => e.offsetParent).map((e) => e.innerText.trim().slice(0, 100)),
  publishButtons: [...document.querySelectorAll('button')].filter((b) => /publish|save draft|update release/i.test(b.innerText)).map((b) => b.innerText.trim()),
  latestBox: [...document.querySelectorAll('input[type=checkbox]')].map((c) => (c.name || c.id) + "=" + c.checked),
}));
console.log(s);
