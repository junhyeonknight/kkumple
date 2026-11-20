const task = await taskSpace(1);
const page = task.page("p1");
const snap = await page.snapshot({ scope: "full_page" });
console.log(snap.split("\n").filter((l) => /Publish|Save draft|latest|pre-release/i.test(l)).join("\n"));
