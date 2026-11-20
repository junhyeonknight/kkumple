const task = await taskSpace(1);
const page = task.page("p1");
await page.click('button:has-text("Publish release")', { label: "publish release 2.0.7" });
await page.waitForURL("**/releases/tag/bundle-v2.0.7", { timeout: 60000 });
console.log({ url: await page.url() });
// 공개 자산 해시 확인
const crypto = await import("node:crypto");
const fs = await import("node:fs/promises");
const rel = JSON.parse(await fs.readFile("E:/꿈플-원격스위치/auto-update/releases/2.0.7/signed/release-info.json", "utf8"));
for (const p of rel.packages) {
  const r = await fetch(p.url, { redirect: "follow" });
  const buf = Buffer.from(await r.arrayBuffer());
  console.log(p.family, r.status, buf.length === p.bytes, crypto.createHash("sha256").update(buf).digest("hex") === p.sha256);
}
