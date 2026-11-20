const { uploadDir } = await import("file:///C:/Users/은준현/ego/kkumple-dev-upload-20261120/upload-lib.mjs");
const fs = await import("node:fs/promises");
const task = await taskSpace(5);
const page = task.page("p1");
const base = "C:/Users/은준현/AppData/Local/Temp/claude/84def4da829e2697/0ab849b7-a5a0-4a5a-8403-19075b00aa93/scratchpad/gitstage/dev/";
const tree = await (await fetch("https://api.github.com/repos/junhyeonknight/kkumple/git/trees/main?recursive=1", { headers: { "User-Agent": "kk" } })).json();
const remote = new Set(tree.tree.filter((t) => t.type === "blob").map((t) => t.path));
const dirs = [];
async function walk(rel) {
  for (const d of await fs.readdir(base + rel, { withFileTypes: true })) {
    if (!d.isDirectory()) continue;
    const r = rel + d.name + "/";
    const files = (await fs.readdir(base + r, { withFileTypes: true })).filter((x) => x.isFile()).map((x) => x.name);
    if (files.length && !files.every((n) => remote.has("dev/" + r + n))) dirs.push(r);
    await walk(r);
  }
}
await walk("");
console.log("todo", dirs.length);
for (const rel of dirs) {
  const t0 = Date.now();
  const r = await uploadDir(page, base, rel, "Add dev/" + rel.replace(/\/$/, "") + " (Kkumple handoff)");
  console.log("OK", r.rel, r.files, Math.round((Date.now() - t0) / 1000) + "s");
}
await task.finish({ keep: [] });
