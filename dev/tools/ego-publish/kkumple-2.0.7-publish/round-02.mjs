const task = await taskSpace(1);
const page = task.page("p1");
await page.click('button[aria-label="Tag: Select tag"]', { label: "open tag picker" });
await page.waitForTimeout(800);
const inputs = await page.evaluate(() => [...document.querySelectorAll('input[type=file]')].map((i) => ({
  id: i.id, name: i.name, cls: i.className, multiple: i.multiple, accept: i.accept,
  parent: i.closest('[class]')?.className?.slice(0, 80),
})));
console.log(inputs);
console.log(await page.snapshot());
