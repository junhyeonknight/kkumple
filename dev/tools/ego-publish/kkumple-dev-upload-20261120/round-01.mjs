const task = await taskSpace("kkumple dev handoff upload");
const page = task.page("p1");
const base = "C:/Users/은준현/AppData/Local/Temp/claude/84def4da829e2697/0ab849b7-a5a0-4a5a-8403-19075b00aa93/scratchpad/gitstage/dev/";
await page.goto("https://github.com/junhyeonknight/kkumple/upload/main/dev", { timeout: 60000 });
await page.waitForTimeout(2500);
const info = await page.evaluate(() => ({
  user: document.querySelector('meta[name="user-login"]')?.content || null,
  url: location.href,
  inputs: [...document.querySelectorAll('input[type=file]')].map((i) => ({ id: i.id, name: i.name, multiple: i.multiple, cls: i.className })),
  buttons: [...document.querySelectorAll('button')].map((b) => b.innerText.trim()).filter((t) => t && t.length < 40),
  radios: [...document.querySelectorAll('input[type=radio]')].map((r) => ({ name: r.name, value: r.value, checked: r.checked })),
  texts: [...document.querySelectorAll('input[type=text], textarea')].map((t) => ({ id: t.id, name: t.name, ph: t.placeholder })),
}));
console.log(JSON.stringify(info, null, 1));
console.log({ taskSpaceId: task.spaceId });
