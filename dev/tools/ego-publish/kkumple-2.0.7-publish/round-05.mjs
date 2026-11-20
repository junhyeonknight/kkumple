const task = await taskSpace(1);
const page = task.page("p1");
const state = await page.evaluate(() => ({
  url: location.href,
  tagButton: document.querySelector('button[aria-label^="Tag:"]')?.innerText.replace(/\s+/g, " "),
  tagHidden: [...document.querySelectorAll('input[name="release[tag_name]"]')].map((i) => i.value),
  title: document.querySelector('input[name="release[name]"]')?.value,
  bodyLen: document.querySelector('textarea[name="release[body]"]')?.value.length,
  zipLines: [...document.body.innerText.matchAll(/[^\n]*kkumple-2\.0\.7[^\n]*/g)].map((m) => m[0]).slice(0, 20),
  uploading: /Uploading/i.test(document.body.innerText),
}));
console.log(state);
await page.screenshot({ path: "C:/Users/은준현/ego/kkumple-2.0.7-publish/state-05.png" });
