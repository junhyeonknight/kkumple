const { uploadDir } = await import("file:///C:/Users/은준현/ego/kkumple-dev-upload-20261120/upload-lib.mjs");
const fs = await import("node:fs/promises");
const task = await taskSpace(5);
const page = task.page("p1");
const base = "C:/Users/은준현/AppData/Local/Temp/claude/84def4da829e2697/0ab849b7-a5a0-4a5a-8403-19075b00aa93/scratchpad/gitstage/dev/";
const dirs = [];
async function walk(rel) {
  for (const d of await fs.readdir(base + rel, { withFileTypes: true })) {
    if (!d.isDirectory()) continue;
    const r = rel + d.name + "/";
    const hasFiles = (await fs.readdir(base + r, { withFileTypes: true })).some((x) => x.isFile());
    if (hasFiles) dirs.push(r);
    await walk(r);
  }
}
await walk("");
console.log("dirs", dirs.length);
for (const rel of dirs) {
  const r = await uploadDir(page, base, rel, "Add dev/" + rel.replace(/\/$/, "") + " (Kkumple handoff)");
  console.log(r.rel, r.files, r.url.slice(-60));
}
await task.finish({ keep: [] });
