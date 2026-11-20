const task = await taskSpace(2);
const page = task.page("p1");
const pos = await page.evaluate(() => {
  const b = [...document.querySelectorAll('button')].find((x) => x.innerText.trim() === "Publish release" && x.getClientRects().length);
  b.scrollIntoView({ block: "center" }); const r = b.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
});
await page.mouse.click(pos.x, pos.y, { label: "publish installer release" });
await page.waitForURL("**/releases/tag/installer-v2.2.5", { timeout: 60000 });
console.log({ url: await page.url() });
const crypto = await import("node:crypto");
const fs = await import("node:fs/promises");
const dir = "E:/꿈플-원격스위치/installer/release/2.2.5/";
for (const [asset, local] of [["kkumple-setup-2.2.5.exe", "kkumple-setup-2.2.5.exe"], ["kkumple-install-2.2.5.zip", "꿈플-배포용-2.2.5.zip"]]) {
  const want = await fs.readFile(dir + local);
  const r = await fetch("https://github.com/junhyeonknight/kkumple/releases/download/installer-v2.2.5/" + asset, { redirect: "follow" });
  const got = Buffer.from(await r.arrayBuffer());
  console.log(asset, r.status, got.length === want.length, crypto.createHash("sha256").update(got).digest("hex") === crypto.createHash("sha256").update(want).digest("hex"));
}
