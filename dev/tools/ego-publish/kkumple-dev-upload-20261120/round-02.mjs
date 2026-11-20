const { uploadDir } = await import("file:///C:/Users/은준현/ego/kkumple-dev-upload-20261120/upload-lib.mjs");
const task = await taskSpace(5);
const page = task.page("p1");
const base = "C:/Users/은준현/AppData/Local/Temp/claude/84def4da829e2697/0ab849b7-a5a0-4a5a-8403-19075b00aa93/scratchpad/gitstage/dev/";
console.log(await uploadDir(page, base, "", "Add Kkumple development handoff notes (dev/)"));
await page.waitForTimeout(2000);
const listing = await page.evaluate(() => [...document.querySelectorAll('a[href*="/blob/main/dev/"]')].map((a) => decodeURIComponent(a.getAttribute("href"))));
console.log([...new Set(listing)]);
