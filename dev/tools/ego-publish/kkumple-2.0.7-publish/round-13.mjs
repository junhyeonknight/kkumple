const task = await taskSpace(1);
const page = task.page("p1");
const pos = await page.evaluate(() => {
  const btn = [...document.querySelectorAll('button')].find((b) => b.innerText.trim() === "Publish release" && b.getClientRects().length);
  btn.scrollIntoView({ block: "center" });
  const r = btn.getBoundingClientRect();
  return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
});
await page.mouse.click(pos.x, pos.y, { label: "publish release 2.0.7" });
await page.waitForURL("**/releases/tag/bundle-v2.0.7", { timeout: 60000 });
console.log({ url: await page.url() });
const crypto = await import("node:crypto");
const fs = await import("node:fs/promises");
const rel = JSON.parse(await fs.readFile("E:/꿈플-원격스위치/auto-update/releases/2.0.7/signed/release-info.json", "utf8"));
for (const p of rel.packages) {
  const r = await fetch(p.url, { redirect: "follow" });
  const buf = Buffer.from(await r.arrayBuffer());
  console.log(p.family, r.status, "bytes", buf.length === p.bytes, "sha", crypto.createHash("sha256").update(buf).digest("hex") === p.sha256);
}
