const task = await taskSpace(1);
const page = task.page("p1");
await page.goto("https://github.com/junhyeonknight/kkumple/releases/edit/untagged-78491b1992297bc1374a", { timeout: 60000 });
await page.waitForLoadState();
const state = await page.evaluate(() => ({
  url: location.href,
  tag: [...document.querySelectorAll('input[name="release[tag_name]"]')].map((i) => i.value),
  title: document.querySelector('input[name="release[name]"]')?.value,
  bodyLen: document.querySelector('textarea[name="release[body]"]')?.value.length,
  zips: [...document.body.innerText.matchAll(/[^\n]*\.zip[^\n]*/g)].map((m) => m[0]).slice(0, 20),
  fileInputs: [...document.querySelectorAll('input[type=file]')].map((i) => i.id),
}));
console.log(state);
