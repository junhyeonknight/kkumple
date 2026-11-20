const task = await taskSpace("kkumple 2.0.7 publish");
const page = task.page("p1");
await page.goto("https://github.com/junhyeonknight/kkumple/releases/new", { timeout: 30000 });
await page.waitForLoadState();
console.log({ taskSpaceId: task.spaceId, page: page.label, url: await page.url(), title: await page.title() });
const login = await page.evaluate(() => ({
  user: document.querySelector('meta[name="user-login"]')?.content || null,
  hasTagInput: !!document.querySelector('input[name="release[tag_name]"], #tag-name, [aria-label*="tag" i]'),
  fileInputs: document.querySelectorAll('input[type=file]').length,
}));
console.log(login);
console.log(await page.snapshot());
